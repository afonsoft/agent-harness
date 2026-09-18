namespace Taskboard.Application.Contracts.Operations;

/// <summary>A single line of a skills/MCP operation log (SPEC-20260918-agent-execution-ux RF-006/RF-007).</summary>
public sealed record OperationLogEntry(DateTimeOffset AtUtc, string Level, string Message);
