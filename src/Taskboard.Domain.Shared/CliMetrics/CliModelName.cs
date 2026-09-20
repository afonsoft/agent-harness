using System.Text.Json;

namespace Taskboard.CliMetrics;

/// <summary>
/// Normalizes the raw <c>ModelName</c> captured by CLI extractors
/// (SPEC-20260920-harness-recurring-jobs RF-002). OpenCode stores a JSON
/// envelope (<c>{"id":"Opus","providerID":"omniroute","variant":"default"}</c>);
/// other CLIs store a plain model name. Returns the lookup key used by the
/// price table.
/// </summary>
public static class CliModelName
{
    /// <summary>Extracts (provider, model). Malformed JSON falls back to the raw string.</summary>
    public static (string? Provider, string? Model) Normalize(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (null, null);
        }

        if (modelName.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(modelName);
                var root = doc.RootElement;
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var provider = root.TryGetProperty("providerID", out var providerEl) ? providerEl.GetString() : null;
                return (provider, id);
            }
            catch (JsonException)
            {
                // Fall through — treat as plain string.
            }
        }

        return (null, modelName);
    }
}
