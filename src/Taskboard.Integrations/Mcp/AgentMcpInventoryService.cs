using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using Taskboard.Agents;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Mcp;
using Tomlyn;
using Tomlyn.Model;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Read-only inventory of every MCP entry each agent CLI currently holds in
/// its user-scope config file (SPEC-20261010-mcp-skills-hub RF-004).
/// Missing/corrupt files degrade to an empty server list — never throw.
/// </summary>
public sealed class AgentMcpInventoryService
{
    private readonly string _homeDirectory;
    private readonly ILogger<AgentMcpInventoryService> _logger;

    public AgentMcpInventoryService(string homeDirectory, ILogger<AgentMcpInventoryService> logger)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
    }

    /// <summary>Inventory across every mapped agent, enum order.</summary>
    public IReadOnlyList<AgentMcpInventoryDto> ListAll()
    {
        var rows = new List<AgentMcpInventoryDto>();
        foreach (var agent in Enum.GetValues<AgentType>())
        {
            var row = Read(agent);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>One agent's inventory, or null when it has no config target.</summary>
    public AgentMcpInventoryDto? Read(AgentType agent)
    {
        var target = AgentMcpConfigMap.GetTarget(agent);
        if (target is null)
        {
            return null;
        }

        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        try
        {
            var servers = target.Style == McpEntryStyle.Continue
                ? ReadContinueDirectory(path)
                : target.Format == McpConfigFormat.Toml
                    ? ReadToml(path)
                    : ReadJson(path, target.ContainerKey);
            return new AgentMcpInventoryDto(
                agent, path, AgentMcpProvisioningService.IsWritable(agent), servers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read MCP inventory for {Agent} at {Path}.", agent, path);
            return new AgentMcpInventoryDto(
                agent, path, AgentMcpProvisioningService.IsWritable(agent), []);
        }
    }

    private static List<AgentMcpServerEntryDto> ReadJson(string path, string containerKey)
    {
        var servers = new List<AgentMcpServerEntryDto>();
        if (!File.Exists(path)
            || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root
            || root[containerKey] is not JsonObject container)
        {
            return servers;
        }

        foreach (var (name, node) in container)
        {
            if (node is JsonObject entry)
            {
                servers.Add(new AgentMcpServerEntryDto(name, TransportOf(entry), DetailOf(entry)));
            }
        }

        return servers;
    }

    private static List<AgentMcpServerEntryDto> ReadToml(string path)
    {
        var servers = new List<AgentMcpServerEntryDto>();
        if (!File.Exists(path))
        {
            return servers;
        }

        var root = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));
        if (root is null
            || !root.TryGetValue("mcp_servers", out var containerObj)
            || containerObj is not TomlTable container)
        {
            return servers;
        }

        foreach (var (name, node) in container)
        {
            if (node is not TomlTable entry)
            {
                continue;
            }

            var url = entry.TryGetValue("url", out var u) ? u as string : null;
            var command = entry.TryGetValue("command", out var c) ? c as string : null;
            var transport = !string.IsNullOrWhiteSpace(command) ? "stdio" : "http";
            var detail = !string.IsNullOrWhiteSpace(command)
                ? command + (entry.TryGetValue("args", out var a) && a is TomlArray args
                    ? " " + string.Join(' ', args.OfType<string>())
                    : string.Empty)
                : url ?? string.Empty;
            servers.Add(new AgentMcpServerEntryDto(name, transport, detail));
        }

        return servers;
    }

    /// <summary>
    /// Continue drops one <c>{ "mcpServers": { &lt;name&gt;: {...} } }</c> JSON
    /// file per server under <c>~/.continue/mcpServers/</c>.
    /// </summary>
    private List<AgentMcpServerEntryDto> ReadContinueDirectory(string directory)
    {
        var servers = new List<AgentMcpServerEntryDto>();
        if (!Directory.Exists(directory))
        {
            return servers;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                servers.AddRange(ReadJson(file, "mcpServers"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed Continue MCP file {File}.", file);
            }
        }

        return servers;
    }

    private static string TransportOf(JsonObject entry)
    {
        var command = entry["command"];
        if (command is JsonArray argv ? argv.Count > 0 : !string.IsNullOrWhiteSpace(command?.GetValue<string>()))
        {
            return "stdio";
        }

        var type = entry["type"]?.GetValue<string>();
        return string.Equals(type, "local", StringComparison.OrdinalIgnoreCase) ? "stdio" : "http";
    }

    private static string DetailOf(JsonObject entry)
    {
        var command = entry["command"];
        if (command is JsonArray argv)
        {
            return string.Join(' ', argv.Select(a => a?.GetValue<string>()).Where(a => !string.IsNullOrEmpty(a)));
        }

        if (!string.IsNullOrWhiteSpace(command?.GetValue<string>()))
        {
            var args = entry["args"] is JsonArray argArray
                ? " " + string.Join(' ', argArray.Select(a => a?.GetValue<string>()).Where(a => !string.IsNullOrEmpty(a)))
                : string.Empty;
            return command.GetValue<string>() + args;
        }

        foreach (var urlKey in (string[])["url", "serverUrl", "httpUrl"])
        {
            var url = entry[urlKey]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return string.Empty;
    }
}
