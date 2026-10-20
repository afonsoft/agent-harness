using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-003: the env a managed hook needs —
/// server callback URL, per-session token, session key. Injected into the
/// child process after <c>WithoutHarnessEnv</c> so only these three
/// HARNESS_AGENT_HOOK_* vars survive the scrub.
/// </summary>
public static class AgentHookEnv
{
    /// <summary>Loopback default — same fallback as the server's own base URL.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:47823";

    /// <summary>
    /// Env vars for a session — returns null when no token service is wired
    /// (tests / minimal hosts), meaning the child gets no hook env.
    /// </summary>
    public static Dictionary<string, string>? Build(
        IAgentHookTokenService? tokens, IConfiguration? configuration, string sessionKey)
    {
        if (tokens is null)
        {
            return null;
        }

        var baseUrl = configuration?["Taskboard:BaseUrl"] ?? DefaultBaseUrl;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARNESS_AGENT_HOOK_URL"] = $"{baseUrl.TrimEnd('/')}/api/agent-hooks",
            ["HARNESS_AGENT_HOOK_TOKEN"] = tokens.Issue(sessionKey),
            ["HARNESS_AGENT_HOOK_SESSION"] = sessionKey,
        };
    }

    /// <summary>Applies the env to a startInfo — no-op when tokens are absent.</summary>
    public static void ApplyTo(
        System.Collections.Generic.IDictionary<string, string?> environment,
        IAgentHookTokenService? tokens, IConfiguration? configuration, string sessionKey)
    {
        var env = Build(tokens, configuration, sessionKey);
        if (env is null)
        {
            return;
        }
        foreach (var (key, value) in env)
        {
            environment[key] = value;
        }
    }
}
