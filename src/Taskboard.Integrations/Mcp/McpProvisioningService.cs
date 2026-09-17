using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Mcp;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Provisions the configured RAG MCP server into the user-scope config file of
/// each enabled agent CLI — idempotent merge, atomic writes, <c>.bak</c>
/// backups and <c>0600</c> permissions (SPEC-20260917-rag-mcp-provisioning).
/// The API key is never logged nor surfaced in status payloads.
/// </summary>
public sealed class McpProvisioningService : IMcpProvisioningService
{
    internal const string ServerNameKey = "Taskboard:Rag:ServerName";
    internal const string UrlKey = "Taskboard:Rag:Url";
    internal const string ApiKeyKey = "Taskboard:Rag:ApiKey";
    internal const string DefaultServerName = "knowledge";

    private sealed record RagConfig(string Name, string? Url, string? ApiKey);

    private sealed record LastRun(
        DateTimeOffset AtUtc,
        long DurationMs,
        IReadOnlyList<McpAgentResult> Agents,
        string? Error);

    private readonly IConfiguration _configuration;
    private readonly ILogger<McpProvisioningService> _logger;
    private readonly string _homeDirectory;
    private readonly Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> _enabledAgentsProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile McpProvisionState _state = McpProvisionState.Idle;
    private volatile LastRun? _lastRun;

    public McpProvisioningService(
        IConfiguration configuration,
        ILogger<McpProvisioningService> logger,
        string homeDirectory,
        Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> enabledAgentsProvider)
    {
        _configuration = configuration;
        _logger = logger;
        _homeDirectory = homeDirectory;
        _enabledAgentsProvider = enabledAgentsProvider;
    }

    public McpProvisionStatus GetStatus()
    {
        var config = ResolveConfig();
        var run = _lastRun;
        var runAgents = run?.Agents ?? [];
        var agents = runAgents
            .Concat(Enum.GetValues<AgentType>()
                .Where(a => runAgents.All(r => r.Agent != a))
                .Select(agent => ReadAgentStatus(agent, config)))
            .ToList();

        return new McpProvisionStatus(
            _state,
            run?.AtUtc,
            run?.DurationMs,
            config.Name,
            config.Url,
            agents,
            run?.Error);
    }

    public void RequestProvision(IReadOnlyCollection<AgentType>? agents = null)
    {
        if (!_gate.Wait(0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProvisionCoreAsync(agents, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public async Task<McpProvisionStatus> ProvisionAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
            return await ProvisionCoreAsync(agents, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpProvisionStatus> ProvisionCoreAsync(
        IReadOnlyCollection<AgentType>? agents,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        _state = McpProvisionState.Running;

        var results = new List<McpAgentResult>();
        string? error = null;
        RagConfig? config = null;

        try
        {
            var targets = agents ?? await _enabledAgentsProvider(cancellationToken).ConfigureAwait(false);
            config = ResolveConfig();
            foreach (var agent in targets.Distinct())
            {
                results.Add(ProvisionAgent(agent, config));
            }

            _state = results.Any(r => r.State == McpAgentState.Failed)
                ? McpProvisionState.Failed
                : McpProvisionState.Succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP provisioning failed.");
            _state = McpProvisionState.Failed;
            error = Sanitize(ex.Message, config?.ApiKey);
        }

        _lastRun = new LastRun(started, stopwatch.ElapsedMilliseconds, results, error);
        return GetStatus();
    }

    private McpAgentResult ProvisionAgent(AgentType agent, RagConfig config)
    {
        var target = AgentMcpConfigMap.GetTarget(agent);
        if (target is null)
        {
            return new McpAgentResult(
                agent, false, string.Empty, null, McpAgentState.Skipped, "no known MCP config target");
        }

        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        var removing = string.IsNullOrWhiteSpace(config.Url);

        try
        {
            var outcome = target.Format == McpConfigFormat.Json
                ? JsonConfigMerger.Merge(
                    path,
                    target.ContainerKey,
                    config.Name,
                    removing ? null : BuildJsonEntry(target.Style, config.Url!, config.ApiKey))
                : TomlConfigMerger.Merge(path, config.Name, removing ? null : config.Url, config.ApiKey);

            var state = outcome switch
            {
                MergeOutcome.Repaired => McpAgentState.Repaired,
                MergeOutcome.Removed => McpAgentState.Removed,
                MergeOutcome.NoChange when removing => McpAgentState.NotConfigured,
                _ => McpAgentState.Configured
            };

            return new McpAgentResult(agent, !removing, path, removing ? null : "http", state, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP provision failed for agent {Agent} at {Path}.", agent, path);
            return new McpAgentResult(
                agent, false, path, null, McpAgentState.Failed, Sanitize(ex.Message, config.ApiKey));
        }
    }

    private McpAgentResult ReadAgentStatus(AgentType agent, RagConfig config)
    {
        var target = AgentMcpConfigMap.GetTarget(agent);
        if (target is null)
        {
            return new McpAgentResult(
                agent, false, string.Empty, null, McpAgentState.Skipped, "no known MCP config target");
        }

        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        try
        {
            if (!File.Exists(path) && Directory.Exists(path))
            {
                return new McpAgentResult(
                    agent, false, path, null, McpAgentState.Failed,
                    "config path is a directory");
            }

            var url = target.Format == McpConfigFormat.Json
                ? JsonConfigMerger.ReadManagedUrl(path, target.ContainerKey, config.Name)
                : TomlConfigMerger.ReadManagedUrl(path, config.Name);

            var configured = url is not null
                && config.Url is not null
                && url.Equals(config.Url, StringComparison.Ordinal);

            return new McpAgentResult(
                agent,
                configured,
                path,
                configured ? "http" : null,
                configured ? McpAgentState.Configured : McpAgentState.NotConfigured,
                null);
        }
        catch (Exception ex)
        {
            return new McpAgentResult(
                agent, false, path, null, McpAgentState.Failed,
                Sanitize(ex.Message, config.ApiKey));
        }
    }

    private static JsonObject BuildJsonEntry(McpEntryStyle style, string url, string? apiKey)
    {
        var entry = new JsonObject();
        switch (style)
        {
            case McpEntryStyle.Devin:
                entry["url"] = url;
                entry["transport"] = "http";
                break;
            case McpEntryStyle.Claude:
                entry["type"] = "http";
                entry["url"] = url;
                break;
            case McpEntryStyle.OpenCode:
                entry["type"] = "remote";
                entry["url"] = url;
                entry["enabled"] = true;
                break;
            case McpEntryStyle.OpenHands:
                entry["url"] = url;
                break;
            default:
                throw new InvalidOperationException($"No JSON entry shape for style '{style}'.");
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            entry["headers"] = new JsonObject
            {
                ["Authorization"] = $"Bearer {apiKey}"
            };
        }

        return entry;
    }

    private RagConfig ResolveConfig()
    {
        var name = ResolveValue(ServerNameKey, "TASKBOARD_RAG_NAME");
        var url = ResolveValue(UrlKey, "TASKBOARD_RAG_URL");
        var key = ResolveValue(ApiKeyKey, "TASKBOARD_RAG_API_KEY");

        return new RagConfig(
            string.IsNullOrWhiteSpace(name) ? DefaultServerName : name.Trim(),
            string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            string.IsNullOrWhiteSpace(key) ? null : key.Trim());
    }

    private string? ResolveValue(string key, string envAlias)
    {
        if (_configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers)
            {
                if (provider is IOverrideConfigurationProvider overrides
                    && overrides.TryGetOverride(key, out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        var env = Environment.GetEnvironmentVariable(envAlias);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return _configuration[key];
    }

    private string Sanitize(string? message, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Operation failed without output.";
        }

        var sanitized = message.Replace(_homeDirectory, "~", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(apiKey))
        {
            sanitized = sanitized.Replace(apiKey, "***", StringComparison.Ordinal);
        }

        return sanitized.Length <= 500 ? sanitized : sanitized[^500..];
    }
}
