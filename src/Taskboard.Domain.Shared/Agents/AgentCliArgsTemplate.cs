namespace Taskboard.Agents;

/// <summary>
/// Renders the <c>argsTemplate</c> of a custom CLI definition into an argv
/// list — whitespace tokenization honouring single/double quotes and the
/// <c>{prompt}</c>/<c>{model}</c> tokens (SPEC-20260928-ai-code-generic-cli).
/// Shared by the domain entity, discovery and the spawn paths.
/// </summary>
public static class AgentCliArgsTemplate
{
    /// <summary>
    /// Tokenizes and renders <paramref name="template"/>: <c>{model}</c> is
    /// replaced when <paramref name="model"/> is supplied (and appended via
    /// <paramref name="modelFlag"/> when the template has no token);
    /// <c>{prompt}</c> is replaced only when <paramref name="prompt"/> is
    /// supplied — interactive (PTY) spawns leave it verbatim-absent.
    /// </summary>
    public static IReadOnlyList<string> Render(
        string? template, string? modelFlag = null, string? prompt = null, string? model = null)
    {
        var args = new List<string>();
        var hasModelToken = template?.Contains("{model}", StringComparison.Ordinal) == true;
        foreach (var token in Split(template))
        {
            var rendered = model is null
                ? token
                : token.Replace("{model}", model, StringComparison.Ordinal);
            if (prompt is not null)
            {
                rendered = rendered.Replace("{prompt}", prompt, StringComparison.Ordinal);
            }

            args.Add(rendered);
        }

        if (model is not null && !string.IsNullOrWhiteSpace(modelFlag) && !hasModelToken)
        {
            args.Add(modelFlag);
            args.Add(model);
        }

        return args;
    }

    /// <summary>Whitespace tokenizer honouring "…" and '…' quoted groups.</summary>
    public static IEnumerable<string> Split(string? template)
    {
        var current = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var c in template ?? string.Empty)
        {
            if (quote is not null)
            {
                quote = ConsumeQuoted(current, c, quote);
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static char? ConsumeQuoted(System.Text.StringBuilder current, char c, char? quote)
    {
        if (c == quote)
        {
            return null;
        }

        current.Append(c);
        return quote;
    }
}
