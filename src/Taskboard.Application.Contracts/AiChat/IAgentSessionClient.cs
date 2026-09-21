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
