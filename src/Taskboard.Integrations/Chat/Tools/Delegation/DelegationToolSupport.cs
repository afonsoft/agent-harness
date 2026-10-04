using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// Shared helpers of the SPEC-20261005 delegation tools: scope resolution,
/// repo-path/git-base capture, recipient computation for inbox reads and the
/// common error envelopes.
/// </summary>
internal static class DelegationToolSupport
{
    public static string ScopeOf(ChatToolContext context) =>
        string.IsNullOrWhiteSpace(context.ConversationId) ? "harness" : context.ConversationId!;

    public static ChatToolResult Error(string message, string reason) =>
        new(JsonSerializer.Serialize(new { error = message }), Refused: true, reason);

    public static ChatToolResult Ok(object payload) =>
        new(JsonSerializer.Serialize(payload));

    /// <summary>
    /// Resolves a user-supplied repo path (absolute or workspace-relative) to
    /// the git toplevel; null when the path is not inside a git checkout.
    /// </summary>
    public static async Task<string?> ResolveGitRootAsync(
        IGitCommandRunner git, string? rawPath, string workspace, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var full = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Join(workspace, rawPath));

        var res = await git.RunAsync(
            full, ["rev-parse", "--show-toplevel"],
            TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return res.ExitCode == 0 ? res.StandardOutput.Trim() : null;
    }

    /// <summary>Current HEAD sha of a git repo; null when unavailable.</summary>
    public static async Task<string?> HeadShaAsync(
        IGitCommandRunner git, string repoPath, CancellationToken ct)
    {
        var res = await git.RunAsync(
            repoPath, ["rev-parse", "HEAD"], TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);
        return res.ExitCode == 0 ? res.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Mailbox recipients the assistant can see: broadcast + idle markers, its
    /// own cli, the scope itself and every task id of the scope.
    /// </summary>
    public static async Task<IReadOnlyCollection<string>> RecipientsAsync(
        IDelegationService service, ChatToolContext context, string scope, CancellationToken ct)
    {
        var recipients = new List<string> { "@all", "@idle", scope };
        if (!string.IsNullOrWhiteSpace(context.DefaultAgentCli))
        {
            recipients.Add(context.DefaultAgentCli!);
        }

        foreach (var task in await service.ListTasksAsync(scope, take: 200, ct).ConfigureAwait(false))
        {
            recipients.Add(task.Id);
        }

        return recipients;
    }
}
