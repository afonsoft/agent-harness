using Taskboard.Harness;

namespace Taskboard.Agents;

/// <summary>
/// Resultado da execução de um agente CLI.
/// </summary>
/// <param name="Usage">Token usage reported by the CLI/API when available (E14 RF-001).</param>
public sealed record AgentExecutionResult(int ExitCode, bool IsSuccess, TokenUsage? Usage = null);
