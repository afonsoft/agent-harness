using Taskboard.Harness;

namespace Taskboard.Agents;

/// <summary>
/// Resultado da execução de um agente CLI.
/// </summary>
/// <param name="Usage">Token usage reported by the CLI/API when available (E14 RF-001).</param>
/// <param name="Duration">Wall-clock duration of the execution (SPEC-20260921 RF-004).</param>
/// <param name="ModelUsed">Model effectively resolved for the run (null = decided by the CLI).</param>
public sealed record AgentExecutionResult(
    int ExitCode,
    bool IsSuccess,
    TokenUsage? Usage = null,
    TimeSpan? Duration = null,
    string? ModelUsed = null);
