using Taskboard.ValueObjects;

namespace Taskboard.Agents;

/// <summary>
/// Adaptador que traduz uma requisição ACP para o comando específico de um CLI de agente.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>
    /// Indica se o adaptador sabe lidar com o tipo de agente.
    /// </summary>
    bool CanHandle(AgentType agentType);

    /// <summary>
    /// Monta o comando local a ser executado a partir da requisição.
    /// </summary>
    AgentCommand BuildCommand(AgentExecutionRequest request);

    /// <summary>
    /// Indica se o adaptador suporta sessões interativas persistentes.
    /// </summary>
    bool SupportsInteractiveSession => false;

    /// <summary>
    /// Monta o comando de inicialização da sessão interativa (stdio/acp).
    /// <paramref name="modelName"/> explicit model picked on the thread; null = CLI default (no flag).
    /// </summary>
    AgentCommand BuildSessionCommand(AgentType agentType, string workdir, Sandbox sandbox, string? modelName = null)
        => throw new NotSupportedException($"Interactive session is not supported for agent {agentType}.");

    /// <summary>
    /// SPEC-20261104-run-terminal-runtime RF-002: launch argv for the CLI's
    /// native interactive TUI (never a one-shot print form) — the run
    /// terminal's first input carries the task text. Returns null when the
    /// CLI has no interactive mode (keeps the streaming path, RF-008).
    /// </summary>
    AgentCommand? BuildInteractiveCommand(
        AgentType agentType, string workdir, string? modelName = null,
        AgentAutonomy autonomy = AgentAutonomy.Edit)
        => null;
}
