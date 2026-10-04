namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-003: composes the user turn for a
/// CLI slash command or CLI-scoped skill — marker chip + tagged body, same
/// wire shape as the skill injection (SPEC-20261001 FR-003). The
/// <c>$ARGUMENTS</c> placeholder (claude/opencode convention) is replaced by
/// the typed args; when the body has no placeholder, args are appended as an
/// "Arguments:" line.
/// </summary>
public static class SlashCommandComposer
{
    public static string ComposeCliCommand(
        string name,
        string source,
        string body,
        string? args,
        string kind = "command")
    {
        var tag = kind == "skill" ? "skill" : "command";
        var substituted = body.Replace("$ARGUMENTS", args ?? string.Empty, StringComparison.Ordinal);
        var composed = $"[{tag}:{name}] {args}\n\n<{tag} name=\"{name}\" source=\"{source}\">\n{substituted}\n</{tag}>";
        if (!string.IsNullOrWhiteSpace(args) && !body.Contains("$ARGUMENTS", StringComparison.Ordinal))
        {
            composed += $"\n\nArguments: {args}";
        }

        return composed;
    }
}
