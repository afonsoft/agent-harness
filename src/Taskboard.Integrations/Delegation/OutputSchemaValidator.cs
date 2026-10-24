using System.Text.Json;
using Json.Schema;

namespace Taskboard.Integrations.Delegation;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-G01: evaluates a worker's final
/// output against the task's <c>output_schema</c>. The verdict only
/// annotates the completion (<c>schema_valid</c>) — a <c>false</c> never
/// discards the child output. Extraction is tolerant: the whole trimmed
/// reply is tried first, then the last fenced code block.
/// </summary>
internal static class OutputSchemaValidator
{
    public static bool Evaluate(string? schemaJson, string? outputText)
    {
        if (string.IsNullOrWhiteSpace(schemaJson) || string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (Exception)
        {
            return false;
        }

        var instance = ExtractJson(outputText);
        if (instance is null)
        {
            return false;
        }

        try
        {
            return schema.Evaluate(
                instance.Value,
                new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whole reply first; then the last ``` fenced block.</summary>
    private static JsonElement? ExtractJson(string text)
    {
        if (TryParse(text.Trim(), out var element))
        {
            return element;
        }

        var fence = LastFence(text);
        return fence is not null && TryParse(fence, out element) ? element : null;
    }

    private static string? LastFence(string text)
    {
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        if (end <= 0)
        {
            return null;
        }

        var start = text.LastIndexOf("```", end - 1, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var body = text[(start + 3)..end];
        var nl = body.IndexOf('\n');
        return nl >= 0 ? body[(nl + 1)..].Trim() : body.Trim();
    }

    private static bool TryParse(string text, out JsonElement element)
    {
        element = default;
        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            element = JsonDocument.Parse(text).RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
