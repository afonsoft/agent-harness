using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Application.Delegation;

/// <summary>
/// SPEC-20261022-mailbox-wake: an addressed mailbox message auto-resumes a
/// hibernated session so its first inbox read sees the delivery. Singleton —
/// resolves its own scope so send paths can fire-and-forget; every failure
/// is logged and swallowed (wake is advisory, never blocks the send).
/// </summary>
public sealed class MailboxWakeService(
    IServiceProvider services,
    ILogger<MailboxWakeService> logger) : IMailboxWakeService
{
    /// <summary>RF-002: per-session re-resume storm throttle.</summary>
    internal static readonly TimeSpan ResumeThrottle = TimeSpan.FromSeconds(60);

    internal const string WorktreePrefix = "@worktree:";
    internal const string WakeReason = "mailbox";

    /// <inheritdoc />
    public async Task WakeForRecipientAsync(string? recipient, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                return;
            }

            recipient = recipient.Trim();
            var isWorktree = recipient.StartsWith(WorktreePrefix, StringComparison.Ordinal);
            // RF-003: broadcast/group addresses never wake — only explicit
            // <cli>, <taskId> and @worktree:<runId> resolve to a session.
            if (!isWorktree && recipient.StartsWith('@'))
            {
                return;
            }

            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var prefs = await AgentHibernationService.ReadPrefsAsync(sp, ct).ConfigureAwait(false);
            if (!prefs.AutoResumeOnMailbox)
            {
                return;
            }

            var records = sp.GetRequiredService<IAgentHibernationRepository>();
            var hibernated = (await records.ListAsync(ct: ct).ConfigureAwait(false))
                .Where(r => r.IsHibernated)
                .ToList();
            if (hibernated.Count == 0)
            {
                return;
            }

            var candidates = isWorktree
                ? await ResolveWorktreeAsync(
                    sp, hibernated, recipient[WorktreePrefix.Length..], ct).ConfigureAwait(false)
                : await ResolveNamedAsync(
                    sp, hibernated, recipient, ct).ConfigureAwait(false);

            var hibernation = sp.GetRequiredService<IAgentHibernationService>();
            var now = DateTime.UtcNow;
            foreach (var candidate in candidates)
            {
                if (candidate.LastAutoResumeUtc is { } last
                    && now - last < ResumeThrottle)
                {
                    continue;
                }

                // Throttle stamps the attempt BEFORE respawning — a failed
                // resume must not spin (RF-002: no crash-loop).
                await records.MarkAutoResumeAttemptAsync(candidate.SessionKey, now, ct)
                    .ConfigureAwait(false);
                var outcome = await hibernation.ResumeAsync(
                    candidate.UserId, candidate.SessionKey,
                    auto: true, WakeReason, ct).ConfigureAwait(false);
                if (!outcome.Resumed)
                {
                    logger.LogDebug(
                        "Mailbox wake: resume of {SessionKey} refused — {Reason}.",
                        candidate.SessionKey, outcome.Reason);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wake is advisory — the delivery itself is already persisted.
            logger.LogDebug(ex, "Mailbox wake skipped for recipient.");
        }
    }

    /// <summary>@worktree:&lt;runId&gt; — session path ↔ record.Cwd join.</summary>
    private static async Task<IReadOnlyList<AgentHibernationDto>> ResolveWorktreeAsync(
        IServiceProvider sp, IReadOnlyList<AgentHibernationDto> hibernated,
        string runId, CancellationToken ct)
    {
        var sessions = sp.GetRequiredService<IWorktreeSessionRepository>();
        var session = await sessions.GetByRunIdAsync(runId, ct).ConfigureAwait(false);
        return session is null
            ? []
            : MatchCwd(hibernated, session.Path);
    }

    /// <summary>&lt;taskId&gt; → task.WorktreeRunId → Cwd join; otherwise the
    /// recipient is a &lt;cli&gt; address — every hibernated session of that
    /// cli is a candidate. Anything else is skipped silently.</summary>
    private static async Task<IReadOnlyList<AgentHibernationDto>> ResolveNamedAsync(
        IServiceProvider sp, IReadOnlyList<AgentHibernationDto> hibernated,
        string recipient, CancellationToken ct)
    {
        var tasks = sp.GetRequiredService<IDelegationTaskRepository>();
        var task = await tasks.GetAsync(recipient, ct).ConfigureAwait(false);
        if (task is not null)
        {
            return task.WorktreeRunId is { } runId
                ? await ResolveWorktreeAsync(sp, hibernated, runId, ct).ConfigureAwait(false)
                : [];
        }

        return hibernated
            .Where(r => string.Equals(r.Cli, recipient, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<AgentHibernationDto> MatchCwd(
        IReadOnlyList<AgentHibernationDto> hibernated, string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, '/');
        return hibernated
            .Where(r => r.Cwd is { } cwd
                && string.Equals(
                    cwd.TrimEnd(Path.DirectorySeparatorChar, '/'),
                    normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
