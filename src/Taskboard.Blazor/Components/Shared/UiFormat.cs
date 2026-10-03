using System.Globalization;

namespace Taskboard.Blazor.Components.Shared;

/// <summary>
/// pt-BR user-facing date/time formats (SPEC-20261003-i18n-consistency RF-003).
/// ISO stays in logs/API; UI pages go through this helper instead of
/// inventing their own format strings.
/// </summary>
public static class UiFormat
{
    public static string DateTime(DateTime value) =>
        value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset value) =>
        DateTime(value.LocalDateTime);

    public static string CompactDateTime(DateTime value) =>
        value.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    public static string CompactDateTime(DateTimeOffset value) =>
        CompactDateTime(value.LocalDateTime);

    public static string Date(DateTime value) =>
        value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset value) =>
        Date(value.LocalDateTime);

    public static string Date(DateOnly value) =>
        value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static readonly CultureInfo UsdCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>USD amounts stay "$" regardless of UI culture — they are prices, not localized currency.</summary>
    public static string Usd(decimal value, int decimals = 4) =>
        value.ToString("C" + decimals, UsdCulture);

    public static string UsdOrDash(decimal? value, int decimals = 4) =>
        value is { } v ? Usd(v, decimals) : "—";
}
