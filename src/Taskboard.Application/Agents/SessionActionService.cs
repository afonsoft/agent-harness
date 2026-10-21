using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-008: delete-from-history with the
/// spec guards — file-per-session CLIs only (claude jsonl, codex rollout),
/// never a live session, path confined to $HOME, audited via a
/// session_deleted feed item.
/// </summary>
public sealed class SessionActionService : ISessionActionService
{
    /// <summary>CLIs whose history entry is exactly one transcript file —
    /// spec open-question 3: db-backed CLIs (devin/opencode) are excluded.</summary>
    internal static readonly HashSet<string> DeletableClis =
        new(StringComparer.Ordinal) { "claude", "codex" };

    private readonly IAgentSessionIndexService _index;
    private readonly ISessionFileActions _files;
    private readonly IAgentFeedService? _feed;
    private readonly ILogger<SessionActionService>? _logger;

    public SessionActionService(
        IAgentSessionIndexService index,
        ISessionFileActions files,
        IAgentFeedService? feed = null,
        ILogger<SessionActionService>? logger = null)
    {
        _index = index;
        _files = files;
        _feed = feed;
        _logger = logger;
    }

    public async Task<SessionDeleteResult> DeleteAsync(
        string userId, string cli, string sessionId, CancellationToken ct = default)
    {
        if (!DeletableClis.Contains(cli))
        {
            return SessionDeleteResult.NotDeletable;
        }

        // Look up the index row — the only safe way to resolve sessionId →
        // transcript path without trusting client-supplied paths.
        var snapshot = await _index.GetAsync(userId, cli: cli, ct: ct).ConfigureAwait(false);
        var row = snapshot.Groups
            .SelectMany(g => g.Sessions)
            .FirstOrDefault(e => string.Equals(e.SessionId, sessionId, StringComparison.Ordinal));
        if (row?.TranscriptPath is null)
        {
            return SessionDeleteResult.NotFound;
        }

        if (row.Live)
        {
            return SessionDeleteResult.Live;
        }

        if (!_files.IsUnderHome(row.TranscriptPath))
        {
            return SessionDeleteResult.OutsideHome;
        }

        await _files.DeleteAsync(row.TranscriptPath, ct).ConfigureAwait(false);
        _index.Invalidate();
        _logger?.LogInformation(
            "session deleted: cli={Cli} id={SessionId} path={Path}", cli, sessionId, row.TranscriptPath);

        if (_feed is not null)
        {
            try
            {
                await _feed.ProduceAsync(new FeedItemRequest(
                    AgentFeedKind.SessionDeleted,
                    $"Session deleted: {row.Title}",
                    $"cli={cli} id={sessionId}",
                    "/agents",
                    SessionKey: null,
                    cli,
                    IssueNumber: null,
                    NeedsAttention: false,
                    userId), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The delete already happened — the audit item is best-effort.
                _logger?.LogDebug(ex, "session_deleted feed item failed");
            }
        }

        return SessionDeleteResult.Deleted;
    }

    public async Task<bool> OpenPathAsync(string path, CancellationToken ct = default) =>
        path is { Length: > 0 } && await _files.OpenAsync(path, ct).ConfigureAwait(false);
}
