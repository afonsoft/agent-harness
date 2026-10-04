namespace Taskboard.Blazor.Services;

/// <summary>A selectable UI locale. <paramref name="Label"/> is the
/// own-language name shown in the picker (Português, English, Español).</summary>
public sealed record LocaleOption(string Culture, string Label);

/// <summary>
/// SPEC-20261008-locale-picker RF-001: current UI locale + string lookup.
/// pt-BR is the product default; the choice persists in localStorage and is
/// applied live (no reload). Subscribe to <see cref="Changed"/> to re-render.
/// </summary>
public interface ILocaleService
{
    /// <summary>Active culture: "pt-BR" (default), "en" or "es".</summary>
    string Culture { get; }

    /// <summary>The locales shown in the picker, in display order.</summary>
    IReadOnlyList<LocaleOption> Options { get; }

    /// <summary>Raised after the active culture changed.</summary>
    event Action? Changed;

    /// <summary>Reads the persisted choice once at startup. Safe to call
    /// more than once and before JS interop is available.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Switches the culture, persists it and applies
    /// <see cref="System.Globalization.CultureInfo"/> defaults +
    /// document.lang. Unknown values are ignored.</summary>
    Task SetCultureAsync(string culture, CancellationToken ct = default);

    /// <summary>Looks up a dotted key (see <c>UiStrings</c>); never empty.</summary>
    string T(string key);

    /// <summary><see cref="T(string)"/> with format arguments.</summary>
    string T(string key, params object?[] args);
}
