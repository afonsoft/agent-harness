using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Hooks;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E03: <c>mcp.call</c> hook on
/// <c>mcp_*</c> — warns when the target server reports degraded health
/// (<c>Meta["healthy"]="false"</c>, populated by
/// <c>ChatMcpClientManager.CallAsync</c> from its live status table). Surfaces
/// a status note only — never blocks (<see cref="IChatHook.BlockOnFailure"/>
/// stays false).
/// </summary>
public sealed class McpHealthCheckHook : IChatHook
{
    public string Name => "mcp-health-check";
    public string Event => ChatHookEvents.McpCall;
    public string Matcher => "mcp_*";

    public Task<HookVerdict> RunAsync(HookContext context, CancellationToken cancellationToken)
    {
        if (context.Meta?.TryGetValue("healthy", out var healthy) == true
            && string.Equals(healthy, "false", StringComparison.OrdinalIgnoreCase))
        {
            var server = context.Meta.TryGetValue("server", out var s) && !string.IsNullOrWhiteSpace(s)
                ? s
                : context.Subject;
            var error = context.Meta.TryGetValue("error", out var e) && !string.IsNullOrWhiteSpace(e)
                ? e
                : "unhealthy";
            return Task.FromResult(HookVerdict.Warn($"mcp server '{server}' is degraded: {error}", Name));
        }

        return Task.FromResult(HookVerdict.Allow);
    }
}
