using Microsoft.Extensions.Configuration;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261023 RF-B03: prompt-defense modes. <see cref="Off"/> is a debug
/// escape hatch (startup warns); <see cref="Sanitize"/> strips invisible/bidi
/// characters; <see cref="SanitizeWrap"/> additionally wraps external content
/// in UNTRUSTED sentinels so the model treats it as data, never instructions.
/// </summary>
public enum PromptDefenseMode
{
    Off,
    Sanitize,
    SanitizeWrap,
}

/// <summary>
/// SPEC-20261023 RF-B02/RF-B03: applies the configured prompt-defense mode to
/// external content before it reaches the model wire. Configuration key:
/// <c>Taskboard:Security:PromptDefense</c> = <c>off|sanitize|sanitize+wrap</c>
/// (default and invalid values resolve to <c>sanitize+wrap</c> — fail-safe).
/// </summary>
public sealed class PromptDefensePolicy(PromptDefenseMode mode)
{
    public const string OpenSentinelPrefix = "<<<UNTRUSTED ";
    public const string CloseSentinel = "<<<END_UNTRUSTED>>>";

    /// <summary>Fallback used by call sites without DI: the secure default.</summary>
    public static readonly PromptDefensePolicy Shared = new(PromptDefenseMode.SanitizeWrap);

    public PromptDefenseMode Mode => mode;

    /// <summary>Parses <c>Taskboard:Security:PromptDefense</c>; unknown values fail safe.</summary>
    public static PromptDefensePolicy FromConfiguration(IConfiguration configuration) =>
        new(configuration["Taskboard:Security:PromptDefense"]?.Trim().ToLowerInvariant() switch
        {
            "off" => PromptDefenseMode.Off,
            "sanitize" => PromptDefenseMode.Sanitize,
            _ => PromptDefenseMode.SanitizeWrap,
        });

    /// <summary>Strips invisible/bidi characters unless the mode is <see cref="PromptDefenseMode.Off"/>.</summary>
    public string Sanitize(string content) =>
        mode == PromptDefenseMode.Off ? content : UntrustedContentSanitizer.Sanitize(content)!;

    /// <summary>Wraps content in UNTRUSTED sentinels only under <see cref="PromptDefenseMode.SanitizeWrap"/>.</summary>
    public string Wrap(string content, string source) =>
        mode == PromptDefenseMode.SanitizeWrap
            ? $"{OpenSentinelPrefix}{source}>>>\n{content}\n{CloseSentinel}"
            : content;

    /// <summary>Sanitize then wrap — the standard pipeline for external text.</summary>
    public string Apply(string content, string source) => Wrap(Sanitize(content), source);
}
