namespace Taskboard.Requests;

/// <summary>
/// Requisição para enviar prompt a uma thread de agente.
/// </summary>
public sealed record PromptAgentThreadRequest(
    string Text,
    string? Delivery = "queue");

/// <summary>
/// Requisição para responder a um pedido de permissão inline de ferramenta.
/// </summary>
public sealed record PermissionReplyRequest(
    string Outcome);
