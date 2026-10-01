using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taskboard.Rendering;

/// <summary>
/// Formats a chat tool result payload for the mini-terminal in the tools
/// collapse (SPEC-20261001-ai-chat-openwebui). Tool results are JSON like
/// <c>{"exitCode":0,"output":"…"}</c> — dumping the raw JSON is unreadable, so
/// this extracts the human-facing fields: free-text payloads verbatim, lists
/// as lines, search results as a numbered list, errors first; anything left
/// falls back to pretty-printed JSON. ANSI escapes are always stripped.
/// </summary>
public static class ToolResultFormatter
{
    private static readonly Regex AnsiEscape = new(
        @"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*(\x07|\x1b\\)|\x1b[PX^_].*?\x1b\\",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> TextKeys = new(StringComparer.Ordinal)
    {
        "output", "content", "text", "value", "result",
    };

    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    /// <summary>Returns terminal-friendly text for a tool result payload.</summary>
    public static string FormatForTerminal(string content)
    {
        var clean = AnsiEscape.Replace(content, string.Empty);
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(clean);
        }
        catch (JsonException)
        {
            return clean;
        }

        using (doc)
        {
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Object => FormatObject(doc.RootElement),
                JsonValueKind.Array => doc.RootElement.GetRawText() is { } raw ? Pretty(raw) : clean,
                _ => clean,
            };
        }
    }

    private static string FormatObject(JsonElement root)
    {
        var output = new StringBuilder();
        var deferred = new StringBuilder();

        // Error surfaces first — it is why the card exists.
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            output.Append("error: ").AppendLine(error.GetString());
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("error"))
            {
                continue;
            }

            AppendProperty(deferred, property);
        }

        output.Append(deferred);
        var text = output.ToString().TrimEnd('\n');
        return text.Length > 0 ? text : Pretty(root.GetRawText());
    }

    private static void AppendProperty(StringBuilder output, JsonProperty property)
    {
        var value = property.Value;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                if (text.Length == 0)
                {
                    return;
                }

                if (TextKeys.Contains(property.Name))
                {
                    output.AppendLine(text);
                }
                else
                {
                    output.Append(property.Name).Append(": ").AppendLine(text);
                }

                break;
            case JsonValueKind.Array:
                AppendArray(output, property);
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                output.Append(property.Name).Append(": ").AppendLine(value.ToString());
                break;
            default:
                output.Append(property.Name).AppendLine(":")
                    .AppendLine(Pretty(value.GetRawText()));
                break;
        }
    }

    private static void AppendArray(StringBuilder output, JsonProperty property)
    {
        var array = property.Value;
        if (property.NameEquals("entries") && array.GetArrayLength() > 0
            && array.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String))
        {
            foreach (var entry in array.EnumerateArray())
            {
                output.AppendLine(entry.GetString());
            }

            return;
        }

        if (property.NameEquals("results") || LooksLikeSearchResults(array))
        {
            var index = 1;
            foreach (var item in array.EnumerateArray())
            {
                AppendSearchResult(output, item, index++);
            }

            return;
        }

        output.Append(property.Name).AppendLine(":")
            .AppendLine(Pretty(array.GetRawText()));
    }

    private static bool LooksLikeSearchResults(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
        && array.GetArrayLength() > 0
        && array.EnumerateArray().All(e =>
            e.ValueKind == JsonValueKind.Object
            && (e.TryGetProperty("url", out _) || e.TryGetProperty("link", out _))
            && (e.TryGetProperty("title", out _) || e.TryGetProperty("snippet", out _)));

    private static void AppendSearchResult(StringBuilder output, JsonElement item, int index)
    {
        var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
        var link = item.TryGetProperty("url", out var u) ? u.GetString()
            : item.TryGetProperty("link", out var l) ? l.GetString() : null;
        var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;

        output.Append(index).Append(". ").AppendLine(title ?? link ?? "(result)");
        if (!string.IsNullOrEmpty(link))
        {
            output.Append("   ").AppendLine(link);
        }

        if (!string.IsNullOrEmpty(snippet))
        {
            output.Append("   ").AppendLine(snippet);
        }
    }

    private static string Pretty(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }
}
