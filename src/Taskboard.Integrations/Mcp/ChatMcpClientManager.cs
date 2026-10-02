using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Chat.Tools;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Default <see cref="IMcpClientManager"/> (SPEC-20261001-chat-mcp-client
/// FR-002): connects the configured servers lazily and in parallel, discovers
/// their tools into <see cref="McpToolAdapter"/>s, dispatches calls with a
/// per-call timeout + one reconnect retry, and redacts/truncates output.
/// Inert when <c>Taskboard:Chat:Mcp:Enabled</c> is false (default).
/// </summary>
public sealed class ChatMcpClientManager : IMcpClientManager
{
    internal const string EnabledKey = "Taskboard:Chat:Mcp:Enabled";
    internal const string ServersKey = "Taskboard:Chat:Mcp:Servers";
    internal const string CallTimeoutKey = "Taskboard:Chat:Mcp:CallTimeoutSeconds";
    internal const int DefaultCallTimeoutSeconds = 30;
    internal const int ConnectTimeoutSeconds = 10;
    internal const int MaxOutputChars = 32 * 1024;

    private static readonly Regex UnsafeNameChars = new(
        "[^a-z0-9_]", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly IConfiguration _configuration;
    private readonly ISecretRedactor? _redactor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ChatMcpClientManager> _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly ConcurrentDictionary<string, McpClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChatMcpServerStatus> _statuses = new(StringComparer.Ordinal);
    // capability target lookup: adapter name → (server, remote tool)
    private readonly ConcurrentDictionary<string, (string Server, string Remote)> _routes = new(StringComparer.Ordinal);
    private readonly object _toolsGate = new();
    private volatile IReadOnlyList<IChatTool> _tools = [];
    private volatile string? _specsFingerprint;
    private volatile bool _connectAttempted;
    private bool _disposed;

    private readonly Func<ChatMcpServerSpec, IClientTransport>? _transportFactory;

    public ChatMcpClientManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ISecretRedactor? redactor = null)
        : this(configuration, loggerFactory, redactor, transportFactory: null)
    {
    }

    /// <summary>Test seam — injects an in-memory transport instead of stdio/http.</summary>
    internal ChatMcpClientManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ISecretRedactor? redactor,
        Func<ChatMcpServerSpec, IClientTransport>? transportFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _redactor = redactor;
        _transportFactory = transportFactory;
        _logger = loggerFactory.CreateLogger<ChatMcpClientManager>();
    }

    private bool Enabled =>
        bool.TryParse(_configuration[EnabledKey], out var enabled) && enabled;

    private TimeSpan CallTimeout
    {
        get
        {
            var seconds = _configuration.GetValue(CallTimeoutKey, DefaultCallTimeoutSeconds);
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 300));
        }
    }

    public IReadOnlyList<ChatMcpServerStatus> GetServers() =>
        _statuses.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<IReadOnlyList<IChatTool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return [];
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return _tools;
    }

    /// <summary>
    /// Parses <c>Taskboard:Chat:Mcp:Servers</c> (JSON array) and appends the
    /// configured RAG server (<c>Taskboard:Rag:Url</c>) so the chat reuses the
    /// same MCP definition provisioned into the agent CLIs.
    /// </summary>
    internal IReadOnlyList<ChatMcpServerSpec> LoadSpecs()
    {
        var specs = new List<ChatMcpServerSpec>();

        var raw = _configuration[ServersKey];
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<ChatMcpServerSpec>>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is not null)
                {
                    specs.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s.Name)));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid {Key} JSON — entry ignored", ServersKey);
            }
        }

        var ragUrl = _configuration["Taskboard:Rag:Url"];
        if (!string.IsNullOrWhiteSpace(ragUrl)
            && specs.All(s => !string.Equals(s.Name, _configuration["Taskboard:Rag:ServerName"] ?? "knowledge", StringComparison.OrdinalIgnoreCase)))
        {
            var ragApiKey = _configuration["Taskboard:Rag:ApiKey"];
            specs.Add(new ChatMcpServerSpec(
                _configuration["Taskboard:Rag:ServerName"] ?? "knowledge",
                Url: ragUrl,
                Headers: string.IsNullOrWhiteSpace(ragApiKey)
                    ? null
                    : new Dictionary<string, string> { ["Authorization"] = $"Bearer {ragApiKey}" }));
        }

        return specs;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        var fingerprint = ComputeSpecsFingerprint(LoadSpecs());
        if (_connectAttempted && string.Equals(fingerprint, _specsFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            fingerprint = ComputeSpecsFingerprint(LoadSpecs());
            if (_connectAttempted && string.Equals(fingerprint, _specsFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            // B-08: configuration changed (hot reload) — drop the previous
            // connections/routes/tools before connecting the new spec set.
            if (_specsFingerprint is not null)
            {
                await DisconnectAllAsync().ConfigureAwait(false);
            }

            _connectAttempted = true;
            _specsFingerprint = fingerprint;
            var specs = LoadSpecs();
            var tasks = specs.Select(spec => ConnectServerAsync(spec, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>Canonical fingerprint of the configured server set — any
    /// config change produces a different value, triggering a reconnect.</summary>
    private static string ComputeSpecsFingerprint(IReadOnlyList<ChatMcpServerSpec> specs) =>
        JsonSerializer.Serialize(specs
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new { s.Name, s.Command, s.Url, s.Args, s.Env, s.Headers }));

    private async Task DisconnectAllAsync()
    {
        foreach (var kv in _clients)
        {
            try { await kv.Value.DisposeAsync().ConfigureAwait(false); }
            catch { /* reload best-effort */ }
        }

        _clients.Clear();
        _routes.Clear();
        _statuses.Clear();
        lock (_toolsGate)
        {
            _tools = [];
        }
    }

    private async Task ConnectServerAsync(ChatMcpServerSpec spec, CancellationToken cancellationToken)
    {
        var transportKind = !string.IsNullOrWhiteSpace(spec.Command) ? "stdio" : "http";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

            var client = await CreateClientAsync(spec, timeout.Token).ConfigureAwait(false);
            var remoteTools = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);

            _clients[spec.Name] = client;
            var adapters = new List<IChatTool>();
            foreach (var tool in remoteTools)
            {
                var exposed = UniqueExposedName(spec.Name, tool.Name);
                var schema = NormalizeSchema(tool.JsonSchema);
                var adapter = new McpToolAdapter(
                    this, spec.Name, tool.Name, exposed, tool.Description ?? tool.Name, schema);
                adapters.Add(adapter);
                _routes[exposed] = (spec.Name, tool.Name);
            }

            // B-08: parallel connects share _tools — mutate under the gate so
            // no adapter list is lost to a read-modify-write race.
            lock (_toolsGate)
            {
                _tools = _tools.Concat(adapters).ToList();
            }
            _statuses[spec.Name] = new ChatMcpServerStatus(spec.Name, transportKind, true, adapters.Count, null);
            _logger.LogInformation("MCP server {Server} connected — {Count} tools", spec.Name, adapters.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _statuses[spec.Name] = new ChatMcpServerStatus(spec.Name, transportKind, false, 0, ex.Message);
            _logger.LogWarning(ex, "MCP server {Server} connect failed", spec.Name);
        }
    }

    private async Task<McpClient> CreateClientAsync(ChatMcpServerSpec spec, CancellationToken cancellationToken)
    {
        IClientTransport transport;
        if (_transportFactory is not null)
        {
            transport = _transportFactory(spec);
        }
        else if (!string.IsNullOrWhiteSpace(spec.Command))
        {
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = spec.Name,
                Command = spec.Command,
                Arguments = spec.Args?.ToList() ?? [],
                EnvironmentVariables = ResolveEnvRefs(spec.Env),
            }, _loggerFactory);
        }
        else if (!string.IsNullOrWhiteSpace(spec.Url))
        {
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = spec.Name,
                Endpoint = new Uri(spec.Url),
                AdditionalHeaders = ResolveEnvRefs(spec.Headers)?
                    .Where(kv => kv.Value is not null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal),
            }, _loggerFactory);
        }
        else
        {
            throw new InvalidOperationException($"MCP server '{spec.Name}' needs command or url");
        }

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>env:VAR</c> values resolve from the process env server-side; entries
    /// whose env var is unset are dropped rather than sent empty.
    /// </summary>
    private static Dictionary<string, string?>? ResolveEnvRefs(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            var effective = value.StartsWith("env:", StringComparison.Ordinal)
                ? Environment.GetEnvironmentVariable(value[4..])
                : value;
            if (effective is not null)
            {
                resolved[key] = effective;
            }
        }

        return resolved;
    }

    private static string NormalizeSchema(JsonElement schema)
    {
        // OpenAI requires object-rooted parameters; wrap anything else.
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "object")
        {
            return schema.GetRawText();
        }

        return $"{{\"type\":\"object\",\"properties\":{{}},\"x-mcp-schema\":{schema.GetRawText()}}}";
    }

    private string UniqueExposedName(string server, string remote)
    {
        var baseName = $"mcp_{UnsafeNameChars.Replace(server.ToLowerInvariant(), "_")}_{UnsafeNameChars.Replace(remote.ToLowerInvariant(), "_")}";
        var name = baseName;
        var suffix = 2;
        while (_routes.ContainsKey(name))
        {
            name = $"{baseName}_{suffix++}";
        }

        return name;
    }

    public async Task<ChatToolResult> CallAsync(
        string serverName,
        string remoteToolName,
        JsonElement arguments,
        ChatToolContext context,
        CancellationToken cancellationToken)
    {
        context.Activity?.Report("running_mcp", $"{serverName}/{remoteToolName}");

        var result = await InvokeAsync(serverName, remoteToolName, arguments, context, cancellationToken, retried: false)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<ChatToolResult> InvokeAsync(
        string server, string remote, JsonElement arguments, ChatToolContext context,
        CancellationToken cancellationToken, bool retried)
    {
        // Server may have failed connect — try one reconnect before giving up.
        if (!_clients.TryGetValue(server, out var client)
            && (!await TryReconnectAsync(server, cancellationToken).ConfigureAwait(false)
                || !_clients.TryGetValue(server, out client)))
        {
            return ErrorResult($"mcp server '{server}' unavailable");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);

            var args = arguments.ValueKind == JsonValueKind.Object
                ? arguments.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value)
                : null;

            var call = await client.CallToolAsync(
                remote, args, cancellationToken: timeout.Token).ConfigureAwait(false);
            return MarshalResult(call);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            context.Activity?.Report("idle", string.Empty);
            return ErrorResult($"mcp call '{server}/{remote}' timed out after {CallTimeout.TotalSeconds}s");
        }
        catch (Exception ex)
        {
            var current = _statuses.GetValueOrDefault(server)
                ?? new ChatMcpServerStatus(server, "unknown", false, 0, null);
            _statuses[server] = current with { Healthy = false, Error = ex.Message };
            if (!retried)
            {
                await TryReconnectAsync(server, cancellationToken).ConfigureAwait(false);
                return await InvokeAsync(server, remote, arguments, context, cancellationToken, retried: true)
                    .ConfigureAwait(false);
            }

            return ErrorResult($"mcp call '{server}/{remote}' failed: {ex.Message}");
        }
    }

    private async Task<bool> TryReconnectAsync(string server, CancellationToken cancellationToken)
    {
        var spec = LoadSpecs().FirstOrDefault(s =>
            string.Equals(s.Name, server, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
        {
            return false;
        }

        // Serialize against a concurrent full reload (EnsureConnectedAsync).
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clients.TryRemove(server, out var dead))
            {
                try { await dead.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
            }

            // Drop the stale routes; ConnectServerAsync re-adds them.
            foreach (var route in _routes.Where(kv => kv.Value.Server == server).Select(kv => kv.Key).ToList())
            {
                _routes.TryRemove(route, out _);
            }

            lock (_toolsGate)
            {
                _tools = _tools.Where(t => !string.Equals(t.Origin, server, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            await ConnectServerAsync(spec, cancellationToken).ConfigureAwait(false);
            return _clients.ContainsKey(server);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private ChatToolResult MarshalResult(CallToolResult result)
    {
        var text = new StringBuilder();
        foreach (var block in result.Content)
        {
            switch (block)
            {
                case TextContentBlock t:
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }

                    text.Append(t.Text);
                    break;
                default:
                    text.Append($"\n[{block.Type} content omitted]");
                    break;
            }
        }

        var payload = text.ToString();
        if (result.StructuredContent is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } structured)
        {
            payload = string.Concat(payload, "\n", structured.GetRawText());
        }

        payload = _redactor?.Redact(payload) ?? payload;
        if (payload.Length > MaxOutputChars)
        {
            payload = string.Concat(payload.AsSpan(0, MaxOutputChars), "\n\n(truncated)");
        }

        var json = JsonSerializer.Serialize(result.IsError == true
            ? new { error = payload }
            : (object)new { result = payload });
        return new ChatToolResult(json, Refused: false, result.IsError == true ? "mcp error" : null);
    }

    private static ChatToolResult ErrorResult(string message) =>
        new(JsonSerializer.Serialize(new { error = message }), Refused: true, "mcp failure");

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var client in _clients.Values)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); } catch { /* shutdown best-effort */ }
        }

        _clients.Clear();
        _connectGate.Dispose();
    }
}
