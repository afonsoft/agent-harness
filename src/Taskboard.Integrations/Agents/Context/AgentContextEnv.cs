using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents.Context;

/// <summary>
/// SPEC-20261104-agent-context-templates RF-001: the <c>HARNESS_*</c> context
/// block a managed agent gets on spawn — own address, scope, task/dispatch
/// ids, API base URL and a scoped callback token. Applied after
/// <c>WithoutHarnessEnv</c> so only these vars survive the scrub.
/// </summary>
public static class AgentContextEnv
{
    /// <summary>Env for a managed agent spawn; null token service → no token.</summary>
    public static Dictionary<string, string> Build(
        IAgentCallbackTokenService? tokens,
        IConfiguration? configuration,
        string sessionKey,
        string scope,
        string? taskId,
        string? dispatchId,
        string agentName,
        IReadOnlyList<string>? capabilities = null)
    {
        var baseUrl = (configuration?["Taskboard:BaseUrl"] ?? AgentHookEnv.DefaultBaseUrl).TrimEnd('/');
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARNESS_SESSION_ID"] = sessionKey,
            ["HARNESS_SCOPE"] = scope,
            ["HARNESS_AGENT_NAME"] = agentName,
            ["HARNESS_API_URL"] = baseUrl,
        };
        if (taskId is not null)
        {
            env["HARNESS_TASK_ID"] = taskId;
        }

        if (dispatchId is not null)
        {
            env["HARNESS_DISPATCH_ID"] = dispatchId;
        }

        if (tokens is not null)
        {
            env["HARNESS_AGENT_TOKEN"] = tokens.Mint(
                sessionKey, scope, taskId, dispatchId, agentName,
                capabilities ??
                [
                    AgentCallbackCapabilities.Mailbox,
                    AgentCallbackCapabilities.Status,
                ]);
        }

        return env;
    }

    /// <summary>Applies the block plus hook env to a startInfo environment.</summary>
    public static void ApplyTo(
        IDictionary<string, string?> environment,
        IAgentCallbackTokenService? callbackTokens,
        IAgentHookTokenService? hookTokens,
        IConfiguration? configuration,
        string sessionKey,
        string scope,
        string? taskId,
        string? dispatchId,
        string agentName)
    {
        foreach (var (key, value) in Build(
                     callbackTokens, configuration, sessionKey, scope, taskId, dispatchId, agentName))
        {
            environment[key] = value;
        }

        AgentHookEnv.ApplyTo(environment, hookTokens, configuration, sessionKey);
    }
}
