using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Current date/time — the open-webui <c>get_current_timestamp</c> builtin
/// (SPEC-20261001-ai-chat-openwebui). Models cannot see the clock; this gives
/// them ISO time, unix epoch and timezone for relative-date questions.
/// </summary>
public sealed class DateTimeTool : IChatTool
{
    public string Name => "current_datetime";
    public string Description =>
        "Get the current date and time (ISO 8601, unix epoch, day of week). "
        + "Pass an IANA timezone id to convert (default: the server local timezone).";
    public string ParametersJson => """
        {"type":"object","properties":{"timezone":{"type":"string","description":"IANA timezone id (e.g. America/Sao_Paulo, Europe/Lisbon) or 'utc'"}}}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var zoneName = arguments.TryGetProperty("timezone", out var t) && t.ValueKind == JsonValueKind.String
            ? (t.GetString() ?? string.Empty).Trim()
            : string.Empty;

        TimeZoneInfo zone;
        try
        {
            zone = zoneName.Length == 0
                ? TimeZoneInfo.Local
                : zoneName.Equals("utc", StringComparison.OrdinalIgnoreCase)
                    ? TimeZoneInfo.Utc
                    : TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"unknown timezone '{zoneName}'" }),
                Refused: true, "bad timezone"));
        }

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
        {
            iso = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            unix = now.ToUnixTimeSeconds(),
            dayOfWeek = now.DayOfWeek.ToString(),
            timezone = zone.Id,
            utcOffsetMinutes = (int)zone.GetUtcOffset(now).TotalMinutes,
            utcIso = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        })));
    }
}
