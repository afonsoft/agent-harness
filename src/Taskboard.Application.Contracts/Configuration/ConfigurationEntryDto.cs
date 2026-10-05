namespace Taskboard.Application.Contracts.Configuration;

/// <summary>One known configuration key with its effective value and provenance.</summary>
/// <param name="Key">Configuration key, e.g. <c>Taskboard:Port</c>.</param>
/// <param name="EffectiveValue">Resolved value; masked when <paramref name="Masked"/> is true.</param>
/// <param name="Source">One of <c>db</c>, <c>env</c>, <c>appsettings</c>, <c>default</c>.</param>
/// <param name="Editable">Whether the key accepts database overrides.</param>
/// <param name="RequiresRestart">Whether a change only applies on next process start.</param>
/// <param name="Masked">Whether <paramref name="EffectiveValue"/> is redacted.</param>
/// <param name="ReadOnlyReason">Why the key cannot be overridden, when not editable.</param>
/// <param name="Group">
/// Display category for the Settings Configuration tab — see
/// <see cref="ConfigurationGroups"/>.
/// </param>
/// <param name="ManagedIn">
/// Internal route of the screen that already owns this key (e.g.
/// <c>/settings?tab=chat</c>), or <c>null</c> when the key is edited on the
/// Configuration tab itself. Entries with a route are hidden from the generic
/// table and surfaced as "managed in other screens" links instead.
/// </param>
public sealed record ConfigurationEntryDto(
    string Key,
    string? EffectiveValue,
    string Source,
    bool Editable,
    bool RequiresRestart,
    bool Masked,
    string? ReadOnlyReason,
    string Group,
    string? ManagedIn);
