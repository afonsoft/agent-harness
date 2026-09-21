using Taskboard.Agents;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Cliente responsável pela comunicação bidirecional com a sessão interativa do CLI do agente.
/// </summary>
public interface IAgentSessionClient
{
    /// <summary>
    /// Inicia uma nova sessão interativa com o processo do agente.
    /// </summary>
    Task<bool> StartSessionAsync(
        string threadId,
        AgentType agentType,
        string workspacePath,
        Sandbox sandbox,
        string? modelName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia um prompt para o agente com entrega imediata ou em fila (steer vs queue).
    /// </summary>
    Task<bool> SendPromptAsync(
        string threadId,
        string text,
        string delivery = "queue",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Solicita o cancelamento da execução corrente na sessão.
    /// </summary>
    Task<bool> CancelAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Responde a um pedido de permissão emitido pelo agente (allow, deny, always).
    /// </summary>
    Task<bool> ReplyPermissionAsync(
        string threadId,
        string requestId,
        string outcome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Altera uma config option da sessão (ACP <c>session/set_config_option</c>)
    /// — modelo, modo, thought_level etc. conforme anunciado pelo agente.
    /// </summary>
    Task<bool> SetConfigOptionAsync(
        string threadId,
        string configId,
        string value,
        bool isBoolean = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// Troca o modo da sessão (ACP <c>session/set_mode</c>) para agentes que
    /// expõem <c>modes</c> em vez de configOptions.
    /// </summary>
    Task<bool> SetModeAsync(
        string threadId,
        string modeId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// Indica se a thread possui uma sessão interativa em execução.
    /// </summary>
    bool IsSessionActive(string threadId);

    /// <summary>
    /// Encerra e limpa a sessão interativa da thread.
    /// </summary>
    Task StopSessionAsync(
        string threadId,
        CancellationToken cancellationToken = default);
}
