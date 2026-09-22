using System.Text.Json;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Reads the live ACP session's <c>configOptions</c> and surfaces the entries
/// with <c>category:"model"</c> as catalog models tagged <c>"acp"</c>
/// (SPEC-20260921-ai-code-thread-config RF-005).
/// </summary>
public sealed class AcpSessionModelCatalog(AcpSessionClient acp) : IAgentSessionModelCatalog
{
    public IReadOnlyList<AiChatModelDto> GetModels(string threadId)
    {
        var peer = acp.GetPeerInfo(threadId);
        if (peer?.ConfigOptions is null)
        {
            return [];
        }

        var agentType = acp.GetSessionAgentType(threadId)?.ToString() ?? "unknown";
        return ParseModels(peer.ConfigOptions, agentType);
    }

    /// <summary>
    /// Turns <c>configOptions</c> entries of category "model" into catalog
    /// entries. Accepts both <c>id</c> (v1) and <c>configId</c> (v2-readiness)
    /// — the category check is what marks an option as a model selector.
    /// </summary>
    public static IReadOnlyList<AiChatModelDto> ParseModels(JsonElement? configOptions, string agentType)
    {
        if (configOptions is not { } opts || opts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AiChatModelDto>();
        foreach (var opt in opts.EnumerateArray())
        {
            if (opt.ValueKind != JsonValueKind.Object
                || !string.Equals(
                    opt.TryGetProperty("category", out var c) ? c.GetString() : null,
                    "model",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (opt.TryGetProperty("options", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in arr.EnumerateArray())
                {
                    if (o.TryGetProperty("value", out var v) && v.GetString() is { } value && value.Length > 0)
                    {
                        models.Add(Entry(agentType, value));
                    }
                }
            }
            else if (opt.TryGetProperty("value", out var single) && single.GetString() is { } current && current.Length > 0)
            {
                models.Add(Entry(agentType, current));
            }
        }

        return models;
    }

    private static AiChatModelDto Entry(string agentType, string name) =>
        new($"{agentType}:{name}", agentType, name, ReasoningEffortSupported: false, agentType, "acp");
}
