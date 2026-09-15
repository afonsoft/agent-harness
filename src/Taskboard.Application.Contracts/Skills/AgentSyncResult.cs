namespace Taskboard.Application.Contracts.Skills;

/// <summary>Per-agent outcome of a synchronization run.</summary>
public sealed record AgentSyncResult(
    string AgentType,
    int Installed,
    int Updated,
    int Skipped,
    string? Error);
