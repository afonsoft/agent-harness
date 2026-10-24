using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D02: provider tool-definition assembly —
/// active schemas sorted by name (stable prefix) plus promoted schemas appended
/// in activation order. Promotion never removes or reorders entries, so the
/// provider's prompt-cache prefix survives activation.
/// </summary>
internal static class DeferredToolWire
{
    public static List<OpenAiToolDefinition> BuildDefinitions(
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatToolPlan? plan,
        IReadOnlyList<string>? promoted)
    {
        var active = plan?.ActiveNames;
        var defs = toolSet
            .Where(kv => active is null || active.Contains(kv.Key))
            .Select(kv => new OpenAiToolDefinition(kv.Key, kv.Value.Description, kv.Value.ParametersJson))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in promoted ?? [])
        {
            if (toolSet.TryGetValue(name, out var tool) && defs.All(d => d.Name != name))
            {
                defs.Add(new OpenAiToolDefinition(name, tool.Description, tool.ParametersJson));
            }
        }

        return defs;
    }
}
