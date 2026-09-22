using System.Text.Json;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Reads the human-facing reason out of an error response body
/// (SPEC-20260922-ai-chat-command-bar RF-003). Handles the API's two error
/// shapes — RFC 7807 <c>problem+json</c> (<c>detail</c>, <c>code</c>) and the
/// legacy inline <c>{ error: { code, message } }</c> envelope — so the UI can
/// toast the real backend reason instead of a generic failure.
/// </summary>
public static class ProblemDetailReader
{
    /// <summary>Extracts <c>detail</c> → <c>code</c> → <c>error.message</c> → <c>null</c>.</summary>
    public static string? TryRead(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString();
            }

            if (root.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(code.GetString()))
            {
                return code.GetString();
            }

            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
