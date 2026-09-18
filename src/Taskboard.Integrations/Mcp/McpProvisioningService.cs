using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Integrations.Agents;
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
    private readonly Func<string, string?> _executableResolver;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> _agyRunner;
    private readonly McpOperationLog? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile McpProvisionState _state = McpProvisionState.Idle;
    private volatile LastRun? _lastRun;

    public McpProvisioningService(
        IConfiguration configuration,
        ILogger<McpProvisioningService> logger,
        string homeDirectory,
        Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> enabledAgentsProvider,
        McpOperationLog? log = null)
        : this(configuration, logger, homeDirectory, enabledAgentsProvider, null, null, log)
    {
    }

    internal McpProvisioningService(
        IConfiguration configuration,
        ILogger<McpProvisioningService> logger,
        string homeDirectory,
        Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> enabledAgentsProvider,
        Func<string, string?>? executableResolver,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>>? agyRunner,
        McpOperationLog? log = null)
    {
        _configuration = configuration;
        _logger = logger;
        _homeDirectory = homeDirectory;
        _enabledAgentsProvider = enabledAgentsProvider;
        _executableResolver = executableResolver ?? PathSearch.FindExecutable;
        _agyRunner = agyRunner ?? RunAgyMcpAsync;
        _log = log;
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

    public void RequestProvision(IReadOnlyCollection<AgentType>? agents = null) =>
        RequestCore(agents, forceRemove: false);

    public void RequestRemoval(IReadOnlyCollection<AgentType>? agents = null) =>
        RequestCore(agents, forceRemove: true);

    private void RequestCore(IReadOnlyCollection<AgentType>? agents, bool forceRemove)
    {
        // Queued, never dropped: a remove requested while a provision is still
        // running must execute after it — dropping it silently left stale
        // entries (SPEC-20260918-rag-mcp-sync).
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ProvisionCoreAsync(agents, forceRemove, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public async Task<McpProvisionStatus> ProvisionAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        bool forceRemove = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
            return await ProvisionCoreAsync(agents, forceRemove, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpProvisionStatus> ProvisionCoreAsync(
        IReadOnlyCollection<AgentType>? agents,
        bool forceRemove,
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
            if (forceRemove)
            {
                config = config with { Url = null, ApiKey = null };
            }

            var mode = string.IsNullOrWhiteSpace(config.Url)
                ? forceRemove ? "remove (explicit)" : "remove"
                : "configure";
            _log?.Info(
                $"MCP provision started — name '{config.Name}', {mode} mode, " +
                $"{targets.Count} target(s).");
            foreach (var agent in targets.Distinct())
            {
                var result = await ProvisionAgentAsync(agent, config, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                var line = $"{agent}: {result.State} — {result.Path}";
                if (result.State == McpAgentState.Failed)
                {
                    _log?.Error($"{line} — {Sanitize(result.Error, config.ApiKey)}");
                }
                else
                {
                    _log?.Info(line);
                }
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
            _log?.Error($"MCP provision failed: {error}");
        }

        _lastRun = new LastRun(started, stopwatch.ElapsedMilliseconds, results, error);
        _log?.Info($"MCP provision finished: {_state} in {stopwatch.ElapsedMilliseconds} ms.");
        return GetStatus();
    }

    private Task<McpAgentResult> ProvisionAgentAsync(AgentType agent, RagConfig config, CancellationToken cancellationToken) =>
        agent switch
        {
            AgentType.Antigravity => ProvisionAntigravityAsync(config, cancellationToken),
            AgentType.Cline => ProvisionClineAsync(config, cancellationToken),
            AgentType.Continue => Task.FromResult(ProvisionContinue(config)),
            _ => Task.FromResult(ProvisionAgent(agent, config))
        };

    /// <summary>
    /// Antigravity is provisioned through its official CLI (`agy mcp add/remove`),
    /// which owns the format of <c>~/.gemini/config/mcp_config.json</c>.
    /// </summary>
    private async Task<McpAgentResult> ProvisionAntigravityAsync(RagConfig config, CancellationToken cancellationToken)
    {
        const string displayPath = "~/.gemini/config/mcp_config.json";
        var agy = _executableResolver("agy");
        if (agy is null)
        {
            return new McpAgentResult(
                AgentType.Antigravity, false, displayPath, null, McpAgentState.Skipped,
                "agy CLI not found on PATH");
        }

        var target = AgentMcpConfigMap.GetTarget(AgentType.Antigravity)!;
        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        var removing = string.IsNullOrWhiteSpace(config.Url);

        try
        {
            var existing = JsonConfigMerger.ReadManagedUrl(path, target.ContainerKey, config.Name, "serverUrl");
            if (removing)
            {
                if (existing is null)
                {
                    return new McpAgentResult(
                        AgentType.Antigravity, false, displayPath, null, McpAgentState.NotConfigured, null);
                }

                var (removeExit, removeOutput) = await _agyRunner(
                    agy, ["mcp", "remove", config.Name], cancellationToken).ConfigureAwait(false);
                return removeExit == 0
                    ? new McpAgentResult(
                        AgentType.Antigravity, false, displayPath, null, McpAgentState.Removed, null)
                    : new McpAgentResult(
                        AgentType.Antigravity, false, displayPath, null, McpAgentState.Failed,
                        Sanitize(removeOutput, config.ApiKey));
            }

            if (existing is not null && existing.Equals(config.Url, StringComparison.Ordinal))
            {
                return new McpAgentResult(
                    AgentType.Antigravity, true, displayPath, "http", McpAgentState.Configured, null);
            }

            var args = new List<string> { "mcp", "add", "-t", "http" };
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                args.AddRange(["-H", $"Authorization: Bearer {config.ApiKey}"]);
            }

            args.Add(config.Name);
            args.Add(config.Url!);

            var (exitCode, output) = await _agyRunner(agy, args, cancellationToken).ConfigureAwait(false);
            return exitCode == 0
                ? new McpAgentResult(
                    AgentType.Antigravity, true, displayPath, "http",
                    existing is null ? McpAgentState.Configured : McpAgentState.Updated, null)
                : new McpAgentResult(
                    AgentType.Antigravity, false, displayPath, null, McpAgentState.Failed,
                    Sanitize(output, config.ApiKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP provision failed for agent {Agent} via agy CLI.", AgentType.Antigravity);
            return new McpAgentResult(
                AgentType.Antigravity, false, displayPath, null, McpAgentState.Failed,
                Sanitize(ex.Message, config.ApiKey));
        }
    }

    /// <summary>
    /// Cline is provisioned through its official CLI (`cline mcp add/remove`),
    /// which owns the format of <c>~/.cline/data/settings/cline_mcp_settings.json</c>.
    /// </summary>
    private async Task<McpAgentResult> ProvisionClineAsync(RagConfig config, CancellationToken cancellationToken)
    {
        const string displayPath = "~/.cline/data/settings/cline_mcp_settings.json";
        var cline = _executableResolver("cline");
        if (cline is null)
        {
            return new McpAgentResult(
                AgentType.Cline, false, displayPath, null, McpAgentState.Skipped,
                "cline CLI not found on PATH");
        }

        var target = AgentMcpConfigMap.GetTarget(AgentType.Cline)!;
        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        var removing = string.IsNullOrWhiteSpace(config.Url);

        try
        {
            var existing = JsonConfigMerger.ReadManagedUrl(
                path, target.ContainerKey, config.Name, "url", "transport");
            if (removing)
            {
                if (existing is null)
                {
                    return new McpAgentResult(
                        AgentType.Cline, false, displayPath, null, McpAgentState.NotConfigured, null);
                }

                var (removeExit, removeOutput) = await _agyRunner(
                    cline, ["mcp", "remove", config.Name], cancellationToken).ConfigureAwait(false);
                return removeExit == 0
                    ? new McpAgentResult(
                        AgentType.Cline, false, displayPath, null, McpAgentState.Removed, null)
                    : new McpAgentResult(
                        AgentType.Cline, false, displayPath, null, McpAgentState.Failed,
                        Sanitize(removeOutput, config.ApiKey));
            }

            if (existing is not null && existing.Equals(config.Url, StringComparison.Ordinal))
            {
                return new McpAgentResult(
                    AgentType.Cline, true, displayPath, "http", McpAgentState.Configured, null);
            }

            var args = new List<string> { "mcp", "add", config.Name, "--transport", "http" };
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                args.AddRange(["--header", $"Authorization: Bearer {config.ApiKey}"]);
            }

            args.Add("--yes");
            args.Add(config.Url!);

            var (exitCode, output) = await _agyRunner(cline, args, cancellationToken).ConfigureAwait(false);
            return exitCode == 0
                ? new McpAgentResult(
                    AgentType.Cline, true, displayPath, "http",
                    existing is null ? McpAgentState.Configured : McpAgentState.Updated, null)
                : new McpAgentResult(
                    AgentType.Cline, false, displayPath, null, McpAgentState.Failed,
                    Sanitize(output, config.ApiKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP provision failed for agent {Agent} via cline CLI.", AgentType.Cline);
            return new McpAgentResult(
                AgentType.Cline, false, displayPath, null, McpAgentState.Failed,
                Sanitize(ex.Message, config.ApiKey));
        }
    }

    /// <summary>
    /// Continue picks up JSON MCP config files dropped into
    /// <c>~/.continue/mcpServers/&lt;name&gt;.json</c> automatically — provisioning is a
    /// file write/delete, no CLI invocation needed.
    /// </summary>
    private McpAgentResult ProvisionContinue(RagConfig config)
    {
        var target = AgentMcpConfigMap.GetTarget(AgentType.Continue)!;
        var directory = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        var filePath = Path.Join(directory, $"{config.Name}.json");
        var removing = string.IsNullOrWhiteSpace(config.Url);

        try
        {
            var existing = JsonConfigMerger.ReadManagedUrl(filePath, "mcpServers", config.Name);
            if (removing)
            {
                if (!File.Exists(filePath))
                {
                    return new McpAgentResult(
                        AgentType.Continue, false, filePath, null, McpAgentState.NotConfigured, null);
                }

                File.Delete(filePath);
                return new McpAgentResult(
                    AgentType.Continue, false, filePath, null, McpAgentState.Removed, null);
            }

            if (existing is not null && existing.Equals(config.Url, StringComparison.Ordinal))
            {
                return new McpAgentResult(
                    AgentType.Continue, true, filePath, "streamable-http", McpAgentState.Configured, null);
            }

            var entry = new JsonObject
            {
                ["type"] = "streamable-http",
                ["url"] = config.Url
            };
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                entry["requestOptions"] = new JsonObject
                {
                    ["headers"] = new JsonObject
                    {
                        ["Authorization"] = $"Bearer {config.ApiKey}"
                    }
                };
            }

            var root = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [config.Name] = entry
                }
            };
            SecureConfigWriter.WriteAtomic(
                filePath,
                root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");

            return new McpAgentResult(
                AgentType.Continue, true, filePath, "streamable-http",
                existing is null ? McpAgentState.Configured : McpAgentState.Updated, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP provision failed for agent {Agent} at {Path}.", AgentType.Continue, filePath);
            return new McpAgentResult(
                AgentType.Continue, false, filePath, null, McpAgentState.Failed,
                Sanitize(ex.Message, config.ApiKey));
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAgyMcpAsync(
        string executable, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, stdout.Append(stderr).ToString().Trim());
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
                MergeOutcome.Updated => McpAgentState.Updated,
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
        var readPath = target.Style == McpEntryStyle.Continue
            ? Path.Join(path, $"{config.Name}.json")
            : path;
        try
        {
            if (!File.Exists(readPath) && Directory.Exists(readPath))
            {
                return new McpAgentResult(
                    agent, false, readPath, null, McpAgentState.Failed,
                    "config path is a directory");
            }

            var url = target.Format == McpConfigFormat.Json
                ? JsonConfigMerger.ReadManagedUrl(
                    readPath, target.ContainerKey, config.Name,
                    target.Style switch
                    {
                        McpEntryStyle.Antigravity => "serverUrl",
                        McpEntryStyle.Qwen => "httpUrl",
                        _ => "url"
                    },
                    target.Style == McpEntryStyle.Cline ? "transport" : null)
                : TomlConfigMerger.ReadManagedUrl(readPath, config.Name);

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
            case McpEntryStyle.Kimi:
            case McpEntryStyle.Kiro:
                entry["url"] = url;
                break;
            case McpEntryStyle.Qwen:
                entry["httpUrl"] = url;
                break;
            case McpEntryStyle.Copilot:
                entry["type"] = "http";
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
