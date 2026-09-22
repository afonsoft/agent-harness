namespace Taskboard.Dtos;

/// <summary>Seção de um patch unificado associada a um path (null = bucket "outros").</summary>
public sealed record DiffFileSection(string? Path, string Body);

/// <summary>
/// Split de um patch <c>git diff</c> em seções por arquivo
/// (SPEC-20260921-cockpit-live-logs-explorer-diff RF-004). Cada seção começa
/// num header <c>diff --git</c> e o path é resolvido pelo lado <c>+++ b/</c>
/// (lado <c>--- a/</c> quando o arquivo foi deletado). Conteúdo anterior ao
/// primeiro header — ou seções sem path resolvível — recebe
/// <see cref="DiffFileSection.Path"/> nulo.
/// </summary>
public static class UnifiedDiffParser
{
    public static IReadOnlyList<DiffFileSection> SplitByFile(string? patch)
    {
        var sections = new List<DiffFileSection>();
        if (string.IsNullOrEmpty(patch))
        {
            return sections;
        }

        var lines = patch.Split('\n');
        var body = new List<string>();
        var open = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush(sections, body);
                open = true;
            }

            body.Add(line);
        }

        if (open || body.Count > 0)
        {
            Flush(sections, body);
        }

        return sections;
    }

    private static void Flush(List<DiffFileSection> sections, List<string> body)
    {
        if (body.Count == 0)
        {
            return;
        }

        // Trailing empty line from the final \n — keep body faithful otherwise.
        var text = string.Join('\n', body).TrimEnd('\n');
        sections.Add(new DiffFileSection(ResolvePath(body), text));
        body.Clear();
    }

    private static string? ResolvePath(IReadOnlyList<string> lines)
    {
        string? oldPath = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                oldPath = StripPrefix(line[4..].Trim(), "a/");
                continue;
            }

            if (!line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                continue;
            }

            var newPath = line[4..].Trim();
            return newPath == "/dev/null" ? oldPath : StripPrefix(newPath, "b/");
        }

        return null;
    }

    private static string StripPrefix(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
}
