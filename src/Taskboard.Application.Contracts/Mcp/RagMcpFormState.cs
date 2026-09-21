using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.Application.Contracts.Mcp;

/// <summary>Resolved values for the Settings → RAG / Knowledge MCP form.</summary>
/// <param name="ServerName">Effective server name to display as the input value.</param>
/// <param name="ServerNameSource">Provenance of <paramref name="ServerName"/> (<c>db</c>/<c>env</c>/<c>appsettings</c>/<c>default</c>/<c>status</c>).</param>
/// <param name="Url">Effective MCP URL; <c>null</c> when unconfigured.</param>
/// <param name="UrlSource">Provenance of <paramref name="Url"/>; <c>status</c> means it came from the provisioning engine's resolved config.</param>
/// <param name="ApiKeySet">Whether an API key is stored (the key itself is never exposed).</param>
public sealed record RagMcpFormState(
    string ServerName,
    string? ServerNameSource,
    string? Url,
    string? UrlSource,
    bool ApiKeySet);

/// <summary>
/// Resolves the effective RAG form values (SPEC-20260921-settings-rag-mcp-prefill):
/// the configuration catalog is the primary source; the MCP provisioning status
/// (<see cref="McpProvisionStatus.ConfiguredUrl"/>/<see cref="McpProvisionStatus.ServerName"/>)
/// is the fallback so the form never shows a real URL only as a placeholder.
/// </summary>
public static class RagMcpFormResolver
{
    public const string ServerNameKey = "Taskboard:Rag:ServerName";
    public const string UrlKey = "Taskboard:Rag:Url";
    public const string ApiKeyKey = "Taskboard:Rag:ApiKey";
    public const string DefaultServerName = "knowledge";

    public static RagMcpFormState Resolve(IReadOnlyList<ConfigurationEntryDto> entries, McpProvisionStatus? status)
    {
        var nameEntry = entries.FirstOrDefault(e => e.Key == ServerNameKey);
        var urlEntry = entries.FirstOrDefault(e => e.Key == UrlKey);

        // Overrides are validated after Trim() but stored verbatim; normalize here so
        // the form doesn't flag a whitespace-only diff as an unsaved change.
        var name = nameEntry?.EffectiveValue?.Trim();
        var nameSource = nameEntry?.Source;
        var url = urlEntry?.EffectiveValue?.Trim();
        var urlSource = urlEntry?.Source;

        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(status?.ConfiguredUrl))
        {
            url = status.ConfiguredUrl;
            urlSource = "status";
        }

        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(status?.ServerName))
        {
            name = status.ServerName;
            nameSource = "status";
        }

        var keySet = entries.FirstOrDefault(e => e.Key == ApiKeyKey)?.EffectiveValue is not null;
        return new RagMcpFormState(name ?? DefaultServerName, nameSource, url, urlSource, keySet);
    }
}
