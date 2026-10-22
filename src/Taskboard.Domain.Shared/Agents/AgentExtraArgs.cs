namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261021-agent-automations RF-005: whitelist parser for
/// <c>AgentAutomation.ExtraAgentArgs</c>. Only launch-shaping flags are
/// admitted — <c>--model</c>/<c>--effort</c> aliases map onto the task's
/// launch preferences (resolved per-CLI by <c>IAgentLaunchResolver</c>),
/// <c>--add-dir</c> passes through verbatim to claude-family invocations.
/// Permission, config, prompt, session and output flags are rejected so a
/// scheduled automation can never weaken the host's headless guardrails.
/// </summary>
public static class AgentExtraArgs
{
    /// <summary>Hard token cap — keeps runaway arg strings out of the DB column.</summary>
    public const int MaxTokens = 32;

    private static readonly IReadOnlySet<string> ModelFlags =
        new HashSet<string>(StringComparer.Ordinal) { "--model", "-m", "--model-name" };

    private static readonly IReadOnlySet<string> EffortFlags =
        new HashSet<string>(StringComparer.Ordinal)
        { "--effort", "--reasoning-effort", "--thinking", "--thinking-budget" };

    private const string AddDirFlag = "--add-dir";

    /// <summary>CLIs that accept a verbatim <c>--add-dir</c> passthrough.</summary>
    private static readonly IReadOnlySet<AgentType> AddDirClis =
        new HashSet<AgentType> { AgentType.Claude, AgentType.Antigravity };

    /// <summary>Autonomy flags that carry a value token (<c>--flag value</c>).</summary>
    private static readonly IReadOnlySet<string> AutonomyValueFlags =
        new HashSet<string>(StringComparer.Ordinal)
        { "--permission-mode", "--sandbox", "-s", "--ask-for-approval", "-a", "--respect-workspace-trust" };

    /// <summary>Parsed outcome — <see cref="Error"/> set means the input was refused.</summary>
    public sealed record Result(
        string? Model,
        string? Effort,
        IReadOnlyList<string> AddDirs,
        IReadOnlyList<string> AutonomyArgs,
        string? Error)
    {
        public static Result Fail(string error) => new(null, null, [], [], error);
    }

    /// <summary>
    /// Validates <paramref name="raw"/> against the whitelist of
    /// <paramref name="cli"/> (builtin enum name or custom-def id). Returns
    /// extracted launch preferences; unknown/malformed flags surface in
    /// <see cref="Result.Error"/> as <c>invalid_extra_args: …</c>.
    /// <paramref name="maxAutonomy"/> caps autonomy flags (RF-006): a flag
    /// implying a stronger envelope than the task's level is refused.
    /// </summary>
    public static Result Parse(
        string? raw, string cli, AgentAutonomy maxAutonomy = AgentAutonomy.Edit)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Result(null, null, [], [], null);
        }

        var tokens = Tokenize(raw);
        if (tokens.Count > MaxTokens)
        {
            return Result.Fail($"invalid_extra_args: exceeds {MaxTokens} tokens");
        }

        var builtin = Enum.TryParse(cli, ignoreCase: true, out AgentType parsed)
            ? parsed
            : (AgentType?)null;

        string? model = null;
        string? effort = null;
        var addDirs = new List<string>();
        var autonomyArgs = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var flag = tokens[i];
            var inlineValue = SplitInlineValue(flag, out var name);
            if (!flag.StartsWith('-'))
            {
                return Result.Fail($"invalid_extra_args: unexpected value '{flag}'");
            }

            string? Next()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }
                if (i + 1 >= tokens.Count || tokens[i + 1].StartsWith('-'))
                {
                    return null;
                }
                return tokens[++i];
            }

            if (ModelFlags.Contains(name))
            {
                model = Next();
                if (model is null)
                {
                    return Result.Fail($"invalid_extra_args: '{name}' requires a model value");
                }
                continue;
            }

            if (EffortFlags.Contains(name))
            {
                if (builtin is null)
                {
                    return Result.Fail(
                        $"invalid_extra_args: '{name}' is unsupported for custom cli '{cli}'");
                }
                effort = Next();
                if (effort is null)
                {
                    return Result.Fail($"invalid_extra_args: '{name}' requires an effort value");
                }
                continue;
            }

            if (name.Equals(AddDirFlag, StringComparison.Ordinal))
            {
                if (builtin is null || !AddDirClis.Contains(builtin.Value))
                {
                    return Result.Fail(
                        $"invalid_extra_args: '--add-dir' is unsupported for cli '{cli}'");
                }
                var dir = Next();
                if (dir is null || !Path.IsPathFullyQualified(dir))
                {
                    return Result.Fail(
                        "invalid_extra_args: '--add-dir' requires an absolute path");
                }
                addDirs.Add(dir);
                continue;
            }

            // RF-006 cli-autonomy-profiles: autonomy flags are whitelisted per
            // CLI and capped at the task's resolved level — a flag implying a
            // stronger envelope than maxAutonomy is refused.
            if (builtin is { } b)
            {
                string? flagValue = null;
                if (AutonomyValueFlags.Contains(name))
                {
                    flagValue = Next();
                    if (flagValue is null)
                    {
                        return Result.Fail(
                            $"invalid_extra_args: '{name}' requires a value");
                    }
                }

                if (AgentCliAutonomy.FlagLevel(b, name, flagValue) is { } implied)
                {
                    if (implied > maxAutonomy)
                    {
                        return Result.Fail(
                            $"invalid_extra_args: '{name}' exceeds autonomy '{AgentAutonomyNames.Name(maxAutonomy)}'");
                    }

                    autonomyArgs.Add(flag);
                    if (flagValue is not null && inlineValue is null)
                    {
                        autonomyArgs.Add(flagValue);
                    }
                    continue;
                }
            }

            return Result.Fail($"invalid_extra_args: flag '{name}' not allowed for '{cli}'");
        }

        return new Result(model, effort, addDirs, autonomyArgs, null);
    }

    /// <summary>
    /// Reassembles the pass-through argv (<c>--add-dir</c> pairs) appended to
    /// the builtin CLI invocation — model/effort ride the launch-preference
    /// fields instead.
    /// </summary>
    public static IReadOnlyList<string> PassthroughArgv(Result parsed) =>
        parsed.AddDirs.SelectMany(d => new[] { AddDirFlag, d })
            .Concat(parsed.AutonomyArgs).ToList();

    /// <summary>Splits <c>--flag=value</c> into (name, value); other tokens keep value null.</summary>
    private static string? SplitInlineValue(string token, out string name)
    {
        var eq = token.IndexOf('=');
        name = eq > 0 && token.StartsWith('-') ? token[..eq] : token;
        return eq > 0 && token.StartsWith('-') ? token[(eq + 1)..] : null;
    }

    /// <summary>Whitespace tokenizer honoring single/double quotes.</summary>
    private static List<string> Tokenize(string raw)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;

        foreach (var ch in raw)
        {
            if (quote is { } q)
            {
                if (ch == q)
                {
                    quote = null;
                }
                else
                {
                    current.Append(ch);
                }
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
