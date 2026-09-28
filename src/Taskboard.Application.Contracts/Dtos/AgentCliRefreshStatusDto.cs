namespace Taskboard.Dtos;

/// <summary>
/// SPEC-20260928-agent-cli-probe-background: state of the background CLI probe
/// refresh (versions + model lists). <see cref="LastCompletedAt"/> is null until
/// the first refresh completes.
/// </summary>
public sealed record AgentCliRefreshStatusDto(
    bool Running,
    DateTimeOffset? LastCompletedAt,
    long? LastDurationMs);
