using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261104-agent-context-templates RF-003: scans
/// <c>{templatesDir}/*.md</c> — frontmatter <c>{name, description?, cli?,
/// command?, model?, effort?, interactive?}</c> + body. A bad file is kept in
/// the listing marked with <see cref="AgentTemplateDto.Error"/> — it never
/// breaks the catalog.
/// </summary>
public sealed class AgentTemplateCatalog : IAgentTemplateCatalog
{
    /// <summary>Default dir: <c>~/.agent-harness/templates</c> (SPEC RF-003).</summary>
    public static string DefaultDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".agent-harness", "templates");
    }

    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "name", "description", "cli", "command", "model", "effort", "interactive",
    };

    private readonly string _directory;

    public AgentTemplateCatalog(IConfiguration? configuration = null)
    {
        _directory = configuration?["Taskboard:Agents:TemplatesDir"] ?? DefaultDirectory();
    }

    public AgentTemplateCatalog(string directory)
    {
        _directory = directory;
    }

    public async Task<IReadOnlyList<AgentTemplateDto>> ListAsync(CancellationToken ct = default)
    {
        var templates = new List<AgentTemplateDto>();
        if (!Directory.Exists(_directory))
        {
            return templates;
        }

        foreach (var file in Directory.EnumerateFiles(_directory, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            templates.Add(await ParseFileAsync(file, ct).ConfigureAwait(false));
        }

        return templates;
    }

    public async Task<AgentTemplateDto?> GetAsync(string name, CancellationToken ct = default)
    {
        var file = Path.Combine(_directory, name + ".md");
        if (Path.GetFileName(file) != name + ".md" || !File.Exists(file))
        {
            return null;
        }

        var parsed = await ParseFileAsync(file, ct).ConfigureAwait(false);
        return parsed.Error is null ? parsed : null;
    }

    private static async Task<AgentTemplateDto> ParseFileAsync(string file, CancellationToken ct)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        try
        {
            var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var (meta, body) = Parse(text);
            if (!meta.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                return Errored(stem, "frontmatter 'name' is required");
            }

            if (!string.Equals(name, stem, StringComparison.Ordinal))
            {
                return Errored(stem, $"frontmatter name '{name}' != filename stem '{stem}'");
            }

            meta.TryGetValue("description", out var description);
            meta.TryGetValue("cli", out var cli);
            meta.TryGetValue("command", out var command);
            meta.TryGetValue("model", out var model);
            meta.TryGetValue("effort", out var effort);
            bool? interactive = meta.TryGetValue("interactive", out var rawInteractive)
                && bool.TryParse(rawInteractive, out var b)
                    ? b
                    : null;
            return new AgentTemplateDto(
                name, description, cli, command, model, effort, interactive,
                body.Trim(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Errored(stem, $"read failed: {ex.Message}");
        }
    }

    /// <summary>Splits <c>---\nkey: value\n---\nbody</c>; missing/short header → no meta.</summary>
    private static (Dictionary<string, string> Meta, string Body) Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), text);
        }

        var close = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (close < 0)
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), text);
        }

        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in normalized[4..close].Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            if (Keys.Contains(key))
            {
                meta[key] = line[(colon + 1)..].Trim();
            }
        }

        return (meta, normalized[(close + 4)..]);
    }

    private static AgentTemplateDto Errored(string stem, string error) =>
        new(stem, null, null, null, null, null, null, string.Empty, error);
}
