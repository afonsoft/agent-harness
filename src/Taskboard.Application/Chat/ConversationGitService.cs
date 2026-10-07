using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.GitHub;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261014-chat-git-bar-overview: git state + git actions for the
/// conversation workspace (the dir <see cref="ConversationWorkspaceService"/>
/// resolves — live run worktree first, else the bound repo/workspace path).
/// Pull/push refuse while a run is active on the conversation; create-PR is
/// idempotent per head/base and auto-commits only inside run worktrees.
/// </summary>
public sealed partial class ConversationGitService(
    IRepository<ChatConversation> conversations,
    IRepository<ChatRun> runs,
    ConversationWorkspaceService workspace,
    IChatGitOps git,
    IGitHubService github,
    HybridCache cache)
{
    private static readonly TimeSpan PrCardTtl = TimeSpan.FromSeconds(60);

    [GeneratedRegex(@"github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/pull/(?<num>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrUrlRegex();

    /// <summary>RF-001: chips — repo slug, branch, dirty, ahead/behind, worktree, run state.</summary>
    public async Task<ChatGitStatusDto?> GetStatusAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var runActive = await runs.Query
            .Where(r => r.ConversationId == conversation.Id
                && (r.Status == ChatRunStatus.Queued || r.Status == ChatRunStatus.Running || r.Status == ChatRunStatus.Paused))
            .AnyAsync(cancellationToken).ConfigureAwait(false);

        var ws = await workspace.GetWorkspaceAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (ws is null)
        {
            return null;
        }

        if (ws.Path is null || !ws.IsGit)
        {
            return new ChatGitStatusDto(ws.Path, IsRepo: false, null, null,
                Dirty: false, Ahead: 0, Behind: 0, ws.WorktreeRunId is not null, runActive);
        }

        var (branch, ahead, behind) = await ParseBranchStatusAsync(ws.Path, cancellationToken).ConfigureAwait(false);
        var slug = await git.ResolveRemoteSlugAsync(ws.Path, cancellationToken).ConfigureAwait(false);

        return new ChatGitStatusDto(
            ws.Path, IsRepo: true, slug, branch ?? ws.Branch, ws.Dirty, ahead, behind,
            ws.WorktreeRunId is not null, runActive);
    }

    /// <summary>RF-002/RF-003: <c>git pull</c> / <c>git push</c> — refused during an active run.</summary>
    public async Task<ChatGitOpResult?> RunSyncOpAsync(
        string conversationId, string op, CancellationToken cancellationToken = default)
    {
        if (op is not ("pull" or "push"))
        {
            return new ChatGitOpResult(false, string.Empty, "unsupported-op");
        }

        var refusal = await GateAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (refusal.notFound)
        {
            return null;
        }
        if (refusal.error is not null)
        {
            return new ChatGitOpResult(false, string.Empty, refusal.error);
        }

        var result = await git.RunAsync(refusal.workdir!, [op], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new ChatGitOpResult(true, Tail(result.Stdout, result.Stderr), null)
            : new ChatGitOpResult(false, Tail(result.Stdout, result.Stderr),
                result.TimedOut ? "timed-out" : "git-failed");
    }

    /// <summary>
    /// RF-006: branch list for the picker — the checked-out branch plus every
    /// switchable name (locals first, then remote-only entries; <c>git switch</c>
    /// DWIMs them into tracking branches). Read-only — runs don't gate it.
    /// </summary>
    public async Task<ChatGitBranchesDto?> GetBranchesAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var ws = await workspace.GetWorkspaceAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (ws?.Path is null || !ws.IsGit)
        {
            return new ChatGitBranchesDto(null, []);
        }

        // Full refnames so local branches containing '/' (feature/x) stay intact
        // while remotes strip their <remote>/ prefix.
        var refs = await git.RunAsync(
                ws.Path,
                ["for-each-ref", "--format=%(refname)", "refs/heads/", "refs/remotes/"],
                cancellationToken)
            .ConfigureAwait(false);
        var current = await GitOutAsync(ws.Path, ["branch", "--show-current"], cancellationToken)
            .ConfigureAwait(false);

        var branches = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in refs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name;
            if (line.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                name = line["refs/heads/".Length..];
            }
            else if (line.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                // refs/remotes/<remote>/<name> → offer "<name>" (git switch
                // DWIMs it into a tracking branch); HEAD pointers skip out.
                var remoteRef = line["refs/remotes/".Length..];
                var slash = remoteRef.IndexOf('/');
                if (slash < 0)
                {
                    continue;
                }

                name = remoteRef[(slash + 1)..];
            }
            else
            {
                continue;
            }

            if (name is "HEAD" or "" || !seen.Add(name))
            {
                continue;
            }

            branches.Add(name);
        }

        return new ChatGitBranchesDto(current, branches);
    }

    /// <summary>
    /// RF-006: <c>git switch &lt;branch&gt;</c> then <c>git pull</c> — the bar's
    /// branch picker switches and syncs in one action. Refused during an
    /// active run, like pull/push.
    /// </summary>
    public async Task<ChatGitOpResult?> CheckoutAsync(
        string conversationId, string branch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch) || !BranchNameRegex().IsMatch(branch.Trim()))
        {
            return new ChatGitOpResult(false, string.Empty, "invalid-branch");
        }

        var refusal = await GateAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (refusal.notFound)
        {
            return null;
        }
        if (refusal.error is not null)
        {
            return new ChatGitOpResult(false, string.Empty, refusal.error);
        }

        var checkout = await git.RunAsync(
            refusal.workdir!, ["switch", branch.Trim()], cancellationToken).ConfigureAwait(false);
        if (checkout.ExitCode != 0)
        {
            return new ChatGitOpResult(false, Tail(checkout.Stdout, checkout.Stderr),
                checkout.TimedOut ? "timed-out" : "git-failed");
        }

        var pull = await git.RunAsync(refusal.workdir!, ["pull"], cancellationToken).ConfigureAwait(false);
        var output = Tail(
            checkout.Stdout + "\n" + pull.Stdout,
            checkout.Stderr + "\n" + pull.Stderr);
        return pull.ExitCode == 0
            ? new ChatGitOpResult(true, output, null)
            : new ChatGitOpResult(true, output, "pull-failed");
    }

    /// <summary>
    /// RF-003: create a PR for the workspace head → base. Worktrees auto-commit
    /// pending changes; a plain workspace with a dirty tree fails with
    /// <c>uncommitted-changes</c> + the file list.
    /// </summary>
    public async Task<ChatCreatePrResult?> CreatePullRequestAsync(
        string conversationId, CreateChatPullRequestRequest request, CancellationToken cancellationToken = default)
    {
        var gate = await GateAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (gate.error is not null)
        {
            return new ChatCreatePrResult(false, false, null, null, gate.error);
        }
        if (gate.notFound)
        {
            return null;
        }

        var workdir = gate.workdir!;
        var head = await GitOutAsync(workdir, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(head) || head == "HEAD")
        {
            return new ChatCreatePrResult(false, false, null, null, "no-head");
        }

        var status = await git.RunAsync(workdir, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        var dirty = !string.IsNullOrWhiteSpace(status.Stdout);
        if (dirty && !gate.isWorktree)
        {
            var files = status.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(50).ToList();
            return new ChatCreatePrResult(false, false, null, null, "uncommitted-changes", files);
        }
        if (dirty)
        {
            await git.RunAsync(workdir, ["add", "-A"], cancellationToken).ConfigureAwait(false);
            var commitTitle = string.IsNullOrWhiteSpace(request.Title)
                ? $"harness: {Truncate(gate.conversationTitle ?? "chat run", 70)}"
                : request.Title.Trim();
            var commit = await git.RunAsync(
                workdir, ["commit", "-m", commitTitle], cancellationToken).ConfigureAwait(false);
            if (commit.ExitCode != 0)
            {
                return new ChatCreatePrResult(false, false, null, null, "git-failed");
            }
        }

        var slug = await git.ResolveRemoteSlugAsync(workdir, cancellationToken).ConfigureAwait(false);
        if (slug is null)
        {
            return new ChatCreatePrResult(false, false, null, null, "slug-unresolved");
        }

        var baseBranch = await OriginHeadAsync(workdir, cancellationToken).ConfigureAwait(false) ?? "main";
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"harness: {Truncate(gate.conversationTitle ?? "chat run", 70)}"
            : request.Title.Trim();
        var body = string.IsNullOrWhiteSpace(request.Body)
            ? $"Opened from the Harness chat.\n\n- conversation `{conversationId}`\n- head `{head}` → base `{baseBranch}`\n"
            : request.Body;

        var existing = await github
            .FindOpenPullRequestUrlAsync(slug, head, baseBranch, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new ChatCreatePrResult(true, true, ParsePrNumber(existing), existing, null);
        }

        string url;
        try
        {
            url = await github
                .CreatePullRequestAsync(slug, title, head, baseBranch, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ChatCreatePrResult(false, false, null, null, "github-failed");
        }

        return new ChatCreatePrResult(true, false, ParsePrNumber(url), url, null);
    }

    /// <summary>RF-004: hover card for a github.com PR URL — cached 60s.</summary>
    public async Task<ChatPrCardDto?> GetPrCardAsync(string url, CancellationToken cancellationToken = default)
    {
        var match = PrUrlRegex().Match(url);
        if (!match.Success)
        {
            return null;
        }

        var slug = $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
        var number = int.Parse(match.Groups["num"].Value);
        var card = await cache.GetOrCreateAsync(
            $"chat-pr-card:{slug}:{number}",
            async _ => await github.GetPullRequestCardAsync(slug, number, cancellationToken).ConfigureAwait(false),
            new HybridCacheEntryOptions { Expiration = PrCardTtl }).ConfigureAwait(false);
        return card is null
            ? null
            : new ChatPrCardDto(card.Number, card.Url, card.Title, card.State, card.Merged,
                card.Author, card.ChecksTotal, card.ChecksSucceeded, card.ChecksFailed);
    }

    /// <summary>Shared pre-flight for pull/push/PR — returns the refusal error or the workdir.</summary>
    private async Task<(string? error, string? workdir, bool isWorktree, bool notFound, string? conversationTitle)> GateAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return (null, null, false, true, null);
        }

        var runActive = await runs.Query
            .Where(r => r.ConversationId == conversation.Id
                && (r.Status == ChatRunStatus.Queued || r.Status == ChatRunStatus.Running || r.Status == ChatRunStatus.Paused))
            .AnyAsync(cancellationToken).ConfigureAwait(false);
        if (runActive)
        {
            return ("run-active", null, false, false, null);
        }

        var ws = await workspace.GetWorkspaceAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (ws?.Path is null || !ws.IsGit)
        {
            return ("not-a-repo", null, false, false, null);
        }

        return (null, ws.Path, ws.WorktreeRunId is not null, false, conversation.Title);
    }

    private async Task<(string? branch, int ahead, int behind)> ParseBranchStatusAsync(
        string workdir, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(workdir, ["status", "-sb"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return (null, 0, 0);
        }

        // "## main...origin/main [ahead 2, behind 1]" / "## HEAD (no branch)"
        var first = result.Stdout.Split('\n', 2)[0].TrimStart('#', ' ');
        var branch = first.Split("...", 2)[0].Split(' ', 2)[0];
        var detached = branch is "HEAD" or "No" or "";
        var ahead = AheadRegex().Match(first) is { Success: true } a ? int.Parse(a.Groups[1].Value) : 0;
        var behind = BehindRegex().Match(first) is { Success: true } b ? int.Parse(b.Groups[1].Value) : 0;
        return (detached ? null : branch, ahead, behind);
    }

    private async Task<string?> OriginHeadAsync(string workdir, CancellationToken cancellationToken)
    {
        var value = await GitOutAsync(
            workdir, ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"], cancellationToken).ConfigureAwait(false);
        const string prefix = "origin/";
        return value?.StartsWith(prefix, StringComparison.Ordinal) == true ? value[prefix.Length..] : value;
    }

    private async Task<string?> GitOutAsync(string workdir, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(workdir, args, cancellationToken).ConfigureAwait(false);
        var value = result.Stdout.Trim();
        return result.ExitCode == 0 && value.Length > 0 ? value : null;
    }

    private static string Tail(string stdout, string stderr)
    {
        var joined = string.Concat(stdout, string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr).Trim();
        return joined.Length <= 2000 ? joined : "…" + joined[^2000..];
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

    private static int? ParsePrNumber(string url) =>
        PrUrlRegex().Match(url) is { Success: true } m && int.TryParse(m.Groups["num"].Value, out var n) ? n : null;

    [GeneratedRegex(@"ahead (\d+)")]
    private static partial Regex AheadRegex();

    [GeneratedRegex(@"behind (\d+)")]
    private static partial Regex BehindRegex();

    // RF-006: a branch arg must never parse as a flag — leading '-' rejected
    // (plus whitespace-free git-ref charset, capped).
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$")]
    private static partial Regex BranchNameRegex();
}
