using System.Diagnostics;
using ModelContextProtocol.Client;

namespace Taskboard.Server.Services;

/// <summary>Outcome of a RAG MCP connectivity probe. Never carries secrets.</summary>
public sealed record RagTestResult(bool Ok, long? LatencyMs, int? ToolCount, string? Error);

/// <summary>
/// SPEC-20261003-ops-hardening RF-003: reachability probe for the configured
/// RAG MCP server (<c>Taskboard:Rag:Url</c>/<c>ApiKey</c>). Performs a real MCP
/// handshake (<c>McpClient.CreateAsync</c> + <c>tools/list</c>) with a 10s cap.
/// The connector is injectable so unit tests can stub the network.
/// </summary>
public sealed class RagConnectionProbe
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Connects to <paramref name="endpoint"/> and returns the remote tool count.</summary>
    private readonly Func<Uri, IReadOnlyDictionary<string, string>?, CancellationToken, Task<int>> _connect;

    public RagConnectionProbe()
        : this(DefaultConnectAsync)
    {
    }

    internal RagConnectionProbe(Func<Uri, IReadOnlyDictionary<string, string>?, CancellationToken, Task<int>> connect)
    {
        _connect = connect;
    }

    public async Task<RagTestResult> TestAsync(string? url, string? apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new RagTestResult(false, null, null, "rag-not-configured");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return new RagTestResult(false, null, null, "invalid-url");
        }

        var headers = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" };

        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var toolCount = await _connect(endpoint, headers, timeout.Token);
            return new RagTestResult(true, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, toolCount, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RagTestResult(false, null, null, "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RagTestResult(false, null, null, Sanitize(ex, apiKey));
        }
    }

    private static async Task<int> DefaultConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "rag-probe",
            Endpoint = endpoint,
            AdditionalHeaders = headers is null
                ? null
                : new Dictionary<string, string>(headers, StringComparer.Ordinal),
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return tools.Count;
    }

    private static string Sanitize(Exception ex, string? apiKey)
    {
        var message = ex.Message;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message = message.Replace(apiKey, "***", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(message) ? ex.GetType().Name : message;
    }
}
