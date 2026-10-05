using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261006 RF-002: one resumable on-disk CLI session — claude/codex/
/// opencode transcripts, gemini session files, agy conversation stores and
/// devin's sessions.db (SPEC-20261004-session-scanner-more-clis) — with the
/// native resume command when the CLI supports it.
/// </summary>
public sealed record AgentSessionInfoDto(
    string Cli,
    string SessionId,
    string? WorkingDirectory,
    DateTime ModifiedAtUtc,
    string SourcePath,
    /// <summary>Shell command that resumes the session, e.g. <c>claude --resume abc</c>; null when unsupported.</summary>
    string? ResumeCommand);

/// <summary>Scans installed CLIs' transcript directories for resumable sessions.</summary>
public interface IAgentSessionScanner
{
    /// <summary>
    /// Lists sessions newest-first. <paramref name="cli"/> (optional) filters to
    /// one CLI binary name; missing directories yield an empty list, never errors.
    /// </summary>
    Task<IReadOnlyList<AgentSessionInfoDto>> ScanAsync(
        string? cli = null, int takePerCli = 50, CancellationToken ct = default);
}
