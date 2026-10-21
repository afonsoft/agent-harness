namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-001: one enriched row of the
/// session index — scanner fields plus derived title, repo/branch, size,
/// message count and live state.
/// </summary>
public sealed record SessionIndexEntryDto(
    string Cli,
    string SessionId,
    string Title,
    string? Cwd,
    string? RepoName,
    string? Branch,
    DateTime CreatedAtUtc,
    DateTime ModifiedAtUtc,
    int? MessageCount,
    long SizeBytes,
    string? TranscriptPath,
    /// <summary>Native resume command (e.g. <c>claude --resume abc</c>).</summary>
    string? ResumeCommand,
    /// <summary>True when the CLI has a native-chat (ACP) adapter.</summary>
    bool HasNativeChat,
    /// <summary>True while the session is live in the attention model.</summary>
    bool Live,
    /// <summary>RF-002: ≤160c snippet when the row matched a transcript rg search.</summary>
    string? MatchSnippet = null);

/// <summary>RF-002: one group of the grouped index view.</summary>
public sealed record SessionGroupDto(
    string Key,
    string Label,
    int Count,
    IReadOnlyList<SessionIndexEntryDto> Sessions);

/// <summary>RF-005: per-CLI visibility chip — every discovered CLI + count.</summary>
public sealed record SessionCliChipDto(string Cli, int Count, bool Hidden);

/// <summary>
/// RF-001: bounded git probe for a session's working directory — implemented
/// in Integrations over <c>GitCommandRunner</c> (ArgumentList, 2s timeout).
/// Application must not reference Integrations, hence the contract here.
/// </summary>
public interface IAgentSessionGitProbe
{
    /// <summary>Returns (repoName, branch) — both null when cwd isn't a repo.</summary>
    Task<(string? RepoName, string? Branch)> ProbeAsync(
        string cwd, CancellationToken ct = default);
}

/// <summary>
/// RF-003: a runtime session (terminal PTY or ACP) surfaced to the index for
/// live-join — <paramref name="NativeSessionId"/> is the provider's own id
/// (e.g. claude session uuid) when the runtime knows it.
/// </summary>
public sealed record LiveAgentSessionDto(
    string SessionKey, string? Cli, string? Cwd, string? NativeSessionId = null);

/// <summary>
/// RF-003: inventory of live agent sessions for the live-glyph join —
/// implemented in Server over TerminalSessionManager + AcpSessionClient.
/// </summary>
public interface ILiveAgentSessionCatalog
{
    IReadOnlyList<LiveAgentSessionDto> Snapshot();
}

/// <summary>
/// RF-002: bounded transcript search — `rg` when on PATH, in-process
/// streaming fallback. Paths are confined to $HOME; callers cap matches.
/// </summary>
public interface IAgentTranscriptSearcher
{
    /// <summary>path → ≤160c snippet of the first matching line.</summary>
    Task<IReadOnlyDictionary<string, string>> SearchAsync(
        IReadOnlyList<string> paths, string needle, int maxMatches,
        CancellationToken ct = default);
}

/// <summary>RF-002/RF-005: grouped index page + chips + the hideEmpty flag.</summary>
public sealed record AgentSessionsIndexDto(
    IReadOnlyList<SessionGroupDto> Groups,
    int Total,
    IReadOnlyList<SessionCliChipDto> Chips,
    bool HideEmpty);

/// <summary>RF-005: sessions-view prefs — hideEmpty + hidden CLIs per user.</summary>
public sealed record SessionViewPrefsDto(bool HideEmpty, IReadOnlyList<string> HiddenClis);

/// <summary>RF-005: persistence of per-user CLI visibility rows.</summary>
public interface ISessionCliVisibilityRepository
{
    Task<IReadOnlyList<string>> ListHiddenAsync(string userId, CancellationToken ct = default);

    /// <summary>Idempotent: upserts (userId,cli) hidden flag; deletes the row when unhidden.</summary>
    Task SetHiddenAsync(string userId, string cli, bool hidden, CancellationToken ct = default);
}

/// <summary>
/// SPEC RF-001/RF-002/RF-005: enriched session index — builds on the scanner,
/// enriches titles/repo/branch/size, then groups, sorts and filters.
/// Rebuilds at most every 60s (NFR) unless forced.
/// </summary>
public interface IAgentSessionIndexService
{
    Task<AgentSessionsIndexDto> GetAsync(
        string userId,
        string? cli = null,
        string group = "cli",
        string sort = "modified",
        string? q = null,
        bool? hideEmpty = null,
        bool transcript = false,
        CancellationToken ct = default);

    /// <summary>Forces an index rebuild on the next Get.</summary>
    void Invalidate();

    Task<SessionViewPrefsDto> GetViewPrefsAsync(string userId, CancellationToken ct = default);

    Task<SessionViewPrefsDto> SaveViewPrefsAsync(
        string userId, SessionViewPrefsDto prefs, CancellationToken ct = default);
}
