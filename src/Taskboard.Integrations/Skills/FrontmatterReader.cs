namespace Taskboard.Integrations.Skills;

internal sealed record Frontmatter(string Name, string Description, IReadOnlyList<string> Tools);

internal static class FrontmatterReader
{
    public static Frontmatter? Read(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2 || !lines[0].Trim().Equals("---", StringComparison.Ordinal))
        {
            return null;
        }

        var endIndex = Array.FindIndex(lines, 1, l => l.Trim().Equals("---", StringComparison.Ordinal));
        if (endIndex == -1)
        {
            return null;
        }

        var builder = new Builder();
        for (var i = 1; i < endIndex; i++)
        {
            ApplyLine(builder, lines[i]);
        }

        if (string.IsNullOrWhiteSpace(builder.Name))
        {
            return null;
        }

        return new Frontmatter(builder.Name, builder.Description, builder.Tools.AsReadOnly());
    }

    private sealed class Builder
    {
        public string Name = string.Empty;
        public string Description = string.Empty;
        public List<string> Tools { get; } = new();
        public bool InTools;
    }

    private static void ApplyLine(Builder builder, string line)
    {
        if (TryExtract(line, "name", out var value))
        {
            builder.Name = value;
            builder.InTools = false;
            return;
        }

        if (TryExtract(line, "description", out value))
        {
            builder.Description = value;
            builder.InTools = false;
            return;
        }

        if (TryExtract(line, "tools", out value))
        {
            builder.InTools = true;
            if (!string.IsNullOrWhiteSpace(value))
            {
                // inline array literal such as [Bash, Read]
                builder.Tools.AddRange(ParseInlineArray(value));
            }

            return;
        }

        if (builder.InTools && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
        {
            var tool = line.TrimStart()[2..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(tool))
            {
                builder.Tools.Add(tool);
            }

            return;
        }

        if (builder.InTools && TryExtractKey(line, out _))
        {
            builder.InTools = false;
        }
    }

    private static IEnumerable<string> ParseInlineArray(string value)
    {
        value = value.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            value = value[1..^1];
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static bool TryExtractKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.TrimStart();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        key = trimmed[..colon].Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryExtract(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + ":";
        if (!line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line.Substring(prefix.Length).Trim().Trim('"').Trim('\'');
        return true;
    }
}
