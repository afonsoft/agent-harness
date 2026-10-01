using System.Text.RegularExpressions;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>How an agent CLI prints its headless model list.</summary>
public enum AgentModelListFormat
{
    /// <summary>One model id per line (<c>opencode models</c>).</summary>
    Lines,

    /// <summary><c>id&lt;TAB&gt;Display Name</c> rows (<c>agy models</c>).</summary>
    TabSeparated,

    /// <summary>
    /// <c>devin models list</c>: <c>Name (id)</c> family headers, indented
    /// <c>aliases: a, b</c> lines and indented <c>id  Display [details]</c>
    /// variant rows.
    /// </summary>
    DevinModelsList,
}

/// <summary>Headless command that prints the CLI's available model ids.</summary>
public sealed record AgentModelListProbe(IReadOnlyList<string> Arguments, AgentModelListFormat Format);

/// <summary>
/// Parses the stdout of a model-list probe into model ids. Banners, headers
/// and display text are dropped; only tokens that look like model ids
/// (<c>[A-Za-z0-9][A-Za-z0-9._/+-]*</c>) survive. Order of first appearance is
/// preserved; duplicates (case-insensitive) collapse.
/// </summary>
public static class AgentModelListParser
{
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._/+-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<string> Parse(AgentModelListFormat format, string output)
    {
        return Enumerate(format, output)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> Enumerate(AgentModelListFormat format, string output) =>
        format switch
        {
            AgentModelListFormat.TabSeparated => EnumerateTabSeparated(output),
            AgentModelListFormat.DevinModelsList => EnumerateDevin(output),
            _ => EnumerateLines(output),
        };

    private static IEnumerable<string> EnumerateLines(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (IsId(line))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> EnumerateTabSeparated(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var tab = raw.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            var id = raw[..tab].Trim();
            if (IsId(id))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> EnumerateDevin(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            foreach (var id in ExtractDevinLineIds(raw))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> ExtractDevinLineIds(string raw)
    {
        if (raw.Length == 0)
        {
            yield break;
        }

        if (!char.IsWhiteSpace(raw[0]))
        {
            var open = raw.LastIndexOf('(');
            var close = raw.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                var id = raw[(open + 1)..close].Trim();
                if (IsId(id))
                {
                    yield return id;
                }
            }

            yield break;
        }

        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith("aliases:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var alias in trimmed["aliases:".Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsId))
            {
                yield return alias;
            }

            yield break;
        }

        var first = trimmed.Split(' ', 2)[0];
        if (IsId(first))
        {
            yield return first;
        }
    }

    private static bool IsId(string token) => IdPattern.IsMatch(token);
}
