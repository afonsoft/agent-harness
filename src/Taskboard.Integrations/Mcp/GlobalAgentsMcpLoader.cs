using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using Taskboard.Application.Contracts.Mcp;

namespace Taskboard.Integrations.Mcp;

/// <summary>Result of a <c>~/.agents</c> scan: parsed specs + a combined fingerprint.</summary>
public sealed record GlobalAgentsMcpLoad(
    IReadOnlyList<ChatMcpServerSpec> Specs,
    string Fingerprint)
{
    public static readonly GlobalAgentsMcpLoad Empty = new([], string.Empty);
}

/// <summary>
/// Loads MCP server definitions the user installed globally under
/// <c>~/.agents</c> — the canonical agents-skills layout — so AI Code can call
/// them (SPEC-20261010-mcp-skills-hub RF-001).
///
/// Files scanned, in deterministic order:
/// <list type="bullet">
///   <item><c>~/.agents/mcp.json</c></item>
///   <item><c>~/.agents/mcp_config.json</c></item>
///   <item><c>~/.agents/mcps/**/*.json</c> (recursive, sorted)</item>
/// </list>
///
/// Each file holds a <c>{ "mcpServers": { name: { url|command, args, env, headers } } }</c>
/// map — the same shape Claude/Cursor use. Parsing is lenient: a malformed
/// file is skipped with a warning, an entry without <c>url</c> or
/// <c>command</c> is dropped, and <c>env:VAR</c> values pass through to the
/// chat-side resolver unchanged. Specs are tagged
/// <see cref="ChatMcpServerSpec.Origin"/> <c>"agents-global"</c>.
/// </summary>
public sealed class GlobalAgentsMcpLoader
{
    internal const string OriginTag = "agents-global";

    private static readonly string[] TopLevelFiles = ["mcp.json", "mcp_config.json"];
    private const string McpsDirectory = "mcps";

    private readonly string _agentsDirectory;
    private readonly ILogger<GlobalAgentsMcpLoader>? _logger;

    /// <param name="agentsDirectory">The <c>~/.agents</c> directory to scan.</param>
    public GlobalAgentsMcpLoader(string agentsDirectory, ILogger<GlobalAgentsMcpLoader>? logger = null)
    {
        _agentsDirectory = agentsDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Reads every candidate file. Never throws — IO/parse problems degrade to
    /// a warning and whatever could be loaded.
    /// </summary>
    public GlobalAgentsMcpLoad Load()
    {
        var specs = new List<ChatMcpServerSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fingerprint = new StringBuilder();

        foreach (var file in CandidateFiles())
        {
            try
            {
                var info = new FileInfo(file);
                var content = File.ReadAllText(file);
                fingerprint
                    .Append(Path.GetFileName(file)).Append(':')
                    .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))).Append(':')
                    .Append(info.LastWriteTimeUtc.Ticks).Append(';');

                foreach (var spec in ParseFile(content))
                {
                    if (names.Add(spec.Name))
                    {
                        specs.Add(spec);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Skipping malformed ~/.agents MCP file {File}.", file);
            }
        }

        return new GlobalAgentsMcpLoad(
            specs.AsReadOnly(),
            fingerprint.Length == 0 ? "empty" : fingerprint.ToString());
    }

    private IEnumerable<string> CandidateFiles()
    {
        if (!Directory.Exists(_agentsDirectory))
        {
            yield break;
        }

        foreach (var name in TopLevelFiles)
        {
            var path = Path.Join(_agentsDirectory, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        var mcpsDir = Path.Join(_agentsDirectory, McpsDirectory);
        if (!Directory.Exists(mcpsDir))
        {
            yield break;
        }

        var files = Directory
            .EnumerateFiles(mcpsDir, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            yield return file;
        }
    }

    /// <summary>
    /// Parses one <c>mcpServers</c> map. Throws <see cref="JsonException"/> on
    /// malformed JSON so the caller logs + skips the whole file.
    /// </summary>
    internal static IReadOnlyList<ChatMcpServerSpec> ParseFile(string content)
    {
        var specs = new List<ChatMcpServerSpec>();
        if (JsonNode.Parse(content) is not JsonObject root
            || root["mcpServers"] is not JsonObject servers)
        {
            return specs;
        }

        foreach (var (name, node) in servers)
        {
            if (string.IsNullOrWhiteSpace(name) || node is not JsonObject entry)
            {
                continue;
            }

            var spec = ParseEntry(name, entry);
            if (spec is not null)
            {
                specs.Add(spec);
            }
        }

        return specs;
    }

    /// <summary>
    /// One <c>mcpServers</c> member → spec. Entries without <c>url</c> or
    /// <c>command</c> are dropped (they can never connect).
    /// </summary>
    private static ChatMcpServerSpec? ParseEntry(string name, JsonObject entry)
    {
        var url = ReadString(entry["url"]);
        var command = ReadString(entry["command"]);
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        IReadOnlyList<string>? args = null;
        if (entry["args"] is JsonArray argArray)
        {
            args = argArray
                .Select(ReadString)
                .Where(a => !string.IsNullOrEmpty(a))
                .Cast<string>()
                .ToList();
        }

        return new ChatMcpServerSpec(
            name.Trim(),
            Url: string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            Command: string.IsNullOrWhiteSpace(command) ? null : command.Trim(),
            Args: args,
            Headers: ReadStringMap(entry["headers"]),
            Env: ReadStringMap(entry["env"]),
            Origin: OriginTag);
    }

    /// <summary>String-or-null — tolerates numbers/objects instead of throwing.</summary>
    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonNode? node)
    {
        if (node is not JsonObject map)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            {
                values[key] = text;
            }
        }

        return values.Count == 0 ? null : values;
    }
}
