using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Typed ACP failure taxonomy (SPEC-20260921-acp-v1-conformance RF-013).
/// JSON-RPC error codes map to <see cref="AcpErrorCode"/>; transport/protocol
/// failures get synthetic codes so the UI can react without parsing messages.
/// </summary>
public enum AcpErrorCode
{
    Unknown = 0,
    ParseError,          // -32700
    InvalidRequest,      // -32600
    MethodNotFound,      // -32601
    InvalidParams,       // -32602
    Internal,            // -32603
    RequestCancelled,    // -32800
    AuthRequired,        // agent-defined auth_required error
    TurnTimeout,
    ProcessDied,
    VersionUnsupported,
}

public sealed class AcpException : Exception
{
    public AcpErrorCode Code { get; }
    public string Method { get; }

    public AcpException(AcpErrorCode code, string method, string message)
        : base($"ACP '{method}' failed ({code}): {message}")
    {
        Code = code;
        Method = method;
    }

    public static AcpException FromErrorElement(string method, JsonElement error)
    {
        var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32()
            : 0;
        var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "error" : "error";
        var mapped = code switch
        {
            -32700 => AcpErrorCode.ParseError,
            -32600 => AcpErrorCode.InvalidRequest,
            -32601 => AcpErrorCode.MethodNotFound,
            -32602 => AcpErrorCode.InvalidParams,
            -32603 => AcpErrorCode.Internal,
            -32800 => AcpErrorCode.RequestCancelled,
            _ => message.Contains("auth_required", StringComparison.OrdinalIgnoreCase)
                ? AcpErrorCode.AuthRequired
                : AcpErrorCode.Unknown,
        };
        return new AcpException(mapped, method, message);
    }
}
