using System.Globalization;
using Microsoft.JSInterop;
using Taskboard.Blazor.Localization;

namespace Taskboard.Blazor.Services;

/// <inheritdoc cref="ILocaleService"/>
public sealed class LocaleService : ILocaleService
{
    private readonly IJSRuntime _js;
    private string _culture = UiStrings.DefaultCulture;
    private bool _initialized;

    public LocaleService(IJSRuntime js) => _js = js;

    public string Culture => _culture;

    public IReadOnlyList<LocaleOption> Options { get; } =
    [
        new LocaleOption("pt-BR", "Português"),
        new LocaleOption("en", "English"),
        new LocaleOption("es", "Español"),
    ];

    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var stored = await _js.InvokeAsync<string?>("taskboard.getLocale", ct);
            var normalized = Normalize(stored);
            if (normalized is not null && normalized != _culture)
            {
                ApplyCulture(normalized);
                Changed?.Invoke();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException)
        {
            // JS interop unavailable (prerender) — stay on the product default.
        }
    }

    public async Task SetCultureAsync(string culture, CancellationToken ct = default)
    {
        var normalized = Normalize(culture);
        if (normalized is null || normalized == _culture)
        {
            return;
        }

        ApplyCulture(normalized);
        try
        {
            await _js.InvokeVoidAsync("taskboard.setLocale", ct, normalized);
            await _js.InvokeVoidAsync("taskboard.setHtmlLang", ct, normalized);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException)
        {
            // JS interop unavailable — the in-memory switch still applies.
        }

        Changed?.Invoke();
    }

    public string T(string key) => UiStrings.Get(_culture, key);

    public string T(string key, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo(_culture), UiStrings.Get(_culture, key), args);

    /// <summary>Maps stored/typed values to a supported culture
    /// ("pt" → "pt-BR", "en-US" → "en", …). Null when unsupported.</summary>
    internal static string? Normalize(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        var lower = culture.Trim().ToLowerInvariant();
        return lower switch
        {
            "pt-br" or "pt" => "pt-BR",
            "es" or "es-es" or "es-mx" => "es",
            _ when lower.StartsWith("en", StringComparison.Ordinal) => "en",
            _ => null,
        };
    }

    private void ApplyCulture(string culture)
    {
        _culture = culture;
        var info = CultureInfo.GetCultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = info;
        CultureInfo.DefaultThreadCurrentUICulture = info;
    }
}
