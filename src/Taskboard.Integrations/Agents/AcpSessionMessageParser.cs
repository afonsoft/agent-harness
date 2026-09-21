using System.Text.Json;
using Taskboard.Application.Contracts.AiChat;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Parser para mensagens JSON-RPC emitidas por agentes em sessão interativa (ACP).
/// </summary>
public static class AcpSessionMessageParser
{
    public static (string Method, string Kind, string Content, string? PayloadJson) ParseNotification(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? string.Empty : string.Empty;

        if (!root.TryGetProperty("params", out var p))
        {
            return (method, "message", json, null);
        }

        var kind = p.TryGetProperty("kind", out var k) ? k.GetString() ?? "message" : "message";
        var content = p.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        var payload = p.GetRawText();

        return (method, kind, content, payload);
    }

    public static PermissionRequestInfo? ParsePermissionRequest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Two accepted shapes: the legacy JSON-RPC envelope
        // { params: { requestId, tool, detail, options } } and the normalized
        // flat payload emitted by AcpProtocolParser { requestId, tool, detail, options }.
        var p = root.TryGetProperty("params", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object
            ? wrapped
            : root;

        if (p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var requestId = p.TryGetProperty("requestId", out var r) ? r.GetString() ?? string.Empty : string.Empty;
        var tool = p.TryGetProperty("tool", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var detail = p.TryGetProperty("detail", out var d) ? d.GetString() ?? string.Empty : string.Empty;

        var options = new List<string>();
        if (p.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in opts.EnumerateArray())
            {
                var val = opt.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    options.Add(val);
                }
            }
        }

        if (options.Count == 0)
        {
            options.AddRange(["allow", "deny"]);
        }

        return new PermissionRequestInfo(requestId, tool, detail, options.AsReadOnly());
    }
}
