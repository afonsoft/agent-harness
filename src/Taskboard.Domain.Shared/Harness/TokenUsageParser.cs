using System.Text.Json;


namespace Taskboard.Harness;

/// <summary>
/// Best-effort extraction of token usage from a single stdout line emitted by
/// an agent CLI (SPEC-20260919-ade-observability-finops RF-001 — "se reportados
/// pelo CLI/API"). Understands the common shapes:
///   Claude result:  {"usage":{"input_tokens":..,"output_tokens":..,"cache_*":..}}
///   Codex event:    {"msg":{"type":"token_count","info":{"total_token_usage":{..}}}}
///   OpenAI style:   {"usage":{"prompt_tokens":..,"completion_tokens":..}}
/// The caller keeps the latest non-null value — CLIs that emit cumulative
/// counters report the final total on the last usage line.
/// </summary>
public static class TokenUsageParser
{
    public static TokenUsage? TryExtract(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return FindUsage(doc.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TokenUsage? FindUsage(JsonElement element, int depth)
    {
        if (depth > 4 || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (prop.Name is "usage" or "token_usage" or "total_token_usage")
            {
                var usage = ParseUsageObject(prop.Value);
                if (usage is not null)
                {
                    return usage;
                }
            }

            var nested = FindUsage(prop.Value, depth + 1);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static TokenUsage? ParseUsageObject(JsonElement usage)
    {
        var input = ReadLong(usage, "input_tokens", "prompt_tokens", "input");
        var output = ReadLong(usage, "output_tokens", "completion_tokens", "output");
        var cacheWrite = ReadLong(usage, "cache_creation_input_tokens", "cache_write_tokens", "cache_write_input_tokens");
        var cacheRead = ReadLong(usage, "cache_read_input_tokens", "cached_input_tokens", "cache_read_tokens");

        if (input is null && output is null && cacheWrite is null && cacheRead is null)
        {
            return null;
        }

        return new TokenUsage(input ?? 0, output ?? 0, cacheWrite ?? 0, cacheRead ?? 0);
    }

    private static long? ReadLong(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
            {
                return n;
            }
        }

        return null;
    }
}
