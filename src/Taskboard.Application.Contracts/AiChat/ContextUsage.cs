using System.Text.Json;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// ACP <c>usage_update</c> metric feeding the composer context meter
/// (SPEC-20260921-ai-code-chat-ux RF-003): used tokens, context window size
/// and optional cumulative cost. Hidden when absent.
/// </summary>
public sealed record ContextUsage(long Used, long? Size, double? Cost)
{
    /// <summary>Usage as a 0..1 ratio; null when the window size is unknown.</summary>
    public double? Ratio => Size is > 0 ? (double)Used / Size.Value : null;

    /// <summary>
    /// Parses a metric event payload — accepts both the flat ACP shape
    /// <c>{used,size,cost}</c> and a wrapped <c>{usage:{...}}</c> variant.
    /// Malformed payloads return null (RF-006 graceful degradation).
    /// </summary>
    public static ContextUsage? TryParse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var node = root.TryGetProperty("usage", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object
                ? wrapped
                : root;

            if (!TryLong(node, "used", out var used) && !TryLong(node, "tokens", out used))
            {
                return null;
            }

            TryLong(node, "size", out var size);
            if (size == 0)
            {
                TryLong(node, "contextWindow", out size);
            }

            var cost = node.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble()
                : (double?)null;

            return new ContextUsage(used, size > 0 ? size : null, cost);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryLong(JsonElement el, string name, out long value)
    {
        value = 0;
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
        {
            value = p.GetInt64();
            return true;
        }

        return false;
    }
}
