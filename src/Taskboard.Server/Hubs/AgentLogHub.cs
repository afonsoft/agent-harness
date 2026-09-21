using Microsoft.AspNetCore.SignalR;
using Taskboard.Agents;

namespace Taskboard.Server.Hubs;

/// <summary>
/// Hub SignalR que transmite logs de execução dos agentes CLI para os clientes.
/// </summary>
public sealed class AgentLogHub : Hub
{
    /// <summary>
    /// Método invocado pelo cliente para se inscrever nos logs de uma issue.
    /// </summary>
    public async Task SubscribeToIssue(string issueId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, issueId);
    }

    /// <summary>
    /// Método invocado pelo cliente para cancelar a inscrição nos logs de uma issue.
    /// </summary>
    public async Task UnsubscribeFromIssue(string issueId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, issueId);
    }

    /// <summary>
    /// Inscreve nos eventos normalizados de um escopo (run/thread/issue) —
    /// SPEC-20260921-agent-execution-event-pipeline RF-003.
    /// </summary>
    public async Task SubscribeToScope(string scopeKind, string scopeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{scopeKind}:{scopeId}");
    }

    /// <summary>Cancela a inscrição nos eventos normalizados do escopo.</summary>
    public async Task UnsubscribeFromScope(string scopeKind, string scopeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent:{scopeKind}:{scopeId}");
    }
}
