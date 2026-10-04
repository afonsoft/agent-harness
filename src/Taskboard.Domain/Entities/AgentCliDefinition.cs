using System.Text.RegularExpressions;
using Taskboard.Agents;

namespace Taskboard.Domain.Entities;

/// <summary>
/// User-declared agent CLI persisted via EF Core — makes the AI Code work
/// with any CLI on the host (or in a container), not only the builtin
/// <c>AgentCliMap</c> entries (SPEC-20260928-ai-code-generic-cli RF-002).
/// <para>
/// <see cref="ArgsTemplate"/> tokens: <c>{prompt}</c> (one-shot invocation)
/// and <c>{model}</c> (resolved model name). Unrecognized tokens are left
/// verbatim so CLIs whose flags use braces keep working.
/// </para>
/// </summary>
public sealed class AgentCliDefinition : Entity<string>
{
    /// <summary>Display name shown in pickers; unique per definition.</summary>
    public string DisplayName { get; private set; } = default!;

    /// <summary>Binary resolved on PATH (or absolute path) — never shell-interpreted.</summary>
    public string Executable { get; private set; } = default!;

    /// <summary>
    /// Arguments appended to <see cref="Executable"/> on spawn.
    /// Supports <c>{prompt}</c> and <c>{model}</c> tokens; empty → bare binary.
    /// </summary>
    public string ArgsTemplate { get; private set; } = string.Empty;

    /// <summary><c>"acp"</c> | <c>"pty"</c> — how threads talk to this CLI.</summary>
    public string Transport { get; private set; } = default!;

    /// <summary>Optional flag used to pass the resolved model (e.g. <c>--model</c>).</summary>
    public string? ModelFlag { get; private set; }

    /// <summary>
    /// SPEC-20261004 RF-006: <c>"argv"</c> (default) | <c>"stdin"</c> — how a
    /// delegated prompt reaches the CLI when <see cref="ArgsTemplate"/> carries
    /// no <c>{prompt}</c> token: argv template vs stdin write.
    /// </summary>
    public string PromptDelivery { get; private set; } = "argv";

    /// <summary>
    /// SPEC-20261004 RF-006/RF-008: optional argv that makes the CLI list its
    /// models (one per line) — powers the model picker for this def. Empty
    /// means no probe (the picker falls back to free text).
    /// </summary>
    public string? ModelListArgs { get; private set; }

    /// <summary>Arguments for the version probe (default <c>--version</c>).</summary>
    public string VersionArgs { get; private set; } = "--version";

    public bool Enabled { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private AgentCliDefinition()
        : base(default!)
    {
    }

    private AgentCliDefinition(string id)
        : base(id)
    {
    }

    /// <summary>
    /// Creates a validated definition; the id is derived as <c>custom-&lt;slug&gt;</c>
    /// from <paramref name="displayName"/> via <see cref="SlugFor"/>.
    /// </summary>
    public static AgentCliDefinition Create(
        string displayName,
        string executable,
        string argsTemplate,
        string transport,
        string? modelFlag = null,
        string? versionArgs = null,
        bool enabled = true,
        DateTime? now = null,
        string? promptDelivery = null,
        string? modelListArgs = null)
    {
        var timestamp = now ?? DateTime.UtcNow;
        return new AgentCliDefinition($"custom-{SlugFor(displayName)}")
        {
            DisplayName = RequireNonEmpty(displayName, nameof(displayName)),
            Executable = RequireNonEmpty(executable, nameof(executable)),
            ArgsTemplate = argsTemplate ?? string.Empty,
            Transport = ValidateTransport(transport),
            ModelFlag = string.IsNullOrWhiteSpace(modelFlag) ? null : modelFlag.Trim(),
            VersionArgs = string.IsNullOrWhiteSpace(versionArgs) ? "--version" : versionArgs.Trim(),
            Enabled = enabled,
            PromptDelivery = ValidatePromptDelivery(promptDelivery),
            ModelListArgs = string.IsNullOrWhiteSpace(modelListArgs) ? null : modelListArgs.Trim(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    public void Update(
        string displayName,
        string executable,
        string argsTemplate,
        string transport,
        string? modelFlag,
        string? versionArgs,
        bool enabled,
        DateTime? now = null,
        string? promptDelivery = null,
        string? modelListArgs = null)
    {
        DisplayName = RequireNonEmpty(displayName, nameof(displayName));
        Executable = RequireNonEmpty(executable, nameof(executable));
        ArgsTemplate = argsTemplate ?? string.Empty;
        Transport = ValidateTransport(transport);
        ModelFlag = string.IsNullOrWhiteSpace(modelFlag) ? null : modelFlag.Trim();
        VersionArgs = string.IsNullOrWhiteSpace(versionArgs) ? "--version" : versionArgs.Trim();
        Enabled = enabled;
        PromptDelivery = ValidatePromptDelivery(promptDelivery ?? PromptDelivery);
        ModelListArgs = modelListArgs is null && promptDelivery is null
            ? ModelListArgs
            : string.IsNullOrWhiteSpace(modelListArgs) ? null : modelListArgs.Trim();
        UpdatedAt = now ?? DateTime.UtcNow;
    }

    /// <summary>True when delegated prompts go to stdin instead of argv.</summary>
    public bool DeliversPromptViaStdin =>
        string.Equals(PromptDelivery, "stdin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Slug used to derive the stable id: lowercase, alnum+dash, ≤48 chars.</summary>
    public static string SlugFor(string displayName)
    {
        var slug = Regex.Replace(displayName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Trim('-');
        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        if (slug.Length == 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "DisplayName must contain at least one letter or digit.");
        }

        return slug;
    }

    /// <summary>
    /// Renders <see cref="ArgsTemplate"/> into an argv list —
    /// <see cref="AgentCliArgsTemplate.Render"/> with this definition's flag.
    /// </summary>
    public IReadOnlyList<string> BuildArgs(string? prompt = null, string? model = null) =>
        AgentCliArgsTemplate.Render(ArgsTemplate, ModelFlag, prompt, model);

    private static string ValidateTransport(string transport)
    {
        if (string.Equals(transport, "acp", StringComparison.OrdinalIgnoreCase))
        {
            return "acp";
        }

        if (string.Equals(transport, "pty", StringComparison.OrdinalIgnoreCase))
        {
            return "pty";
        }

        throw new DomainException(
            TaskboardDomainErrorCodes.InvalidValue,
            $"Invalid transport '{transport}' — expected 'acp' or 'pty'.");
    }

    private static string ValidatePromptDelivery(string? promptDelivery)
    {
        if (string.IsNullOrWhiteSpace(promptDelivery)
            || string.Equals(promptDelivery, "argv", StringComparison.OrdinalIgnoreCase))
        {
            return "argv";
        }

        if (string.Equals(promptDelivery, "stdin", StringComparison.OrdinalIgnoreCase))
        {
            return "stdin";
        }

        throw new DomainException(
            TaskboardDomainErrorCodes.InvalidValue,
            $"Invalid promptDelivery '{promptDelivery}' — expected 'argv' or 'stdin'.");
    }

    private static string RequireNonEmpty(string value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"{field} is required.");
}
