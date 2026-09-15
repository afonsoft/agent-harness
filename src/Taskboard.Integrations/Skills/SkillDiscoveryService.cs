using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Integrations.Skills;

public sealed class SkillDiscoveryService : ISkillDiscoveryService
{
    private const int MaxFilesPerSkill = 500;
    private const long MaxFileBytes = 256 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".yaml", ".yml", ".xml", ".csv", ".cs", ".fs",
        ".razor", ".cshtml", ".html", ".css", ".js", ".ts", ".py", ".sh", ".ps1",
        ".toml", ".ini", ".cfg", ".sln", ".csproj", ".fsproj", ".props", ".targets",
        ".gitignore", ".editorconfig", ".mjs", ".jsx", ".tsx"
    };

    private readonly IReadOnlyList<SkillDiscoverySource> _sources;

    public SkillDiscoveryService(IEnumerable<SkillDiscoverySource> sources)
    {
        _sources = sources.ToList().AsReadOnly();
    }

    public Task<IReadOnlyList<SkillDto>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var skills = new List<SkillDto>();

        foreach (var source in _sources)
        {
            if (!Directory.Exists(source.Path))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(source.Path))
            {
                var skillFile = Path.Join(directory, "SKILL.md");
                if (!File.Exists(skillFile))
                {
                    continue;
                }

                var frontmatter = FrontmatterReader.Read(skillFile);
                if (frontmatter is null)
                {
                    continue;
                }

                skills.Add(new SkillDto(
                    frontmatter.Name,
                    frontmatter.Description,
                    source.Source,
                    directory));
            }
        }

        return Task.FromResult<IReadOnlyList<SkillDto>>(skills.AsReadOnly());
    }

    public Task<SkillDetailDto?> GetDetailAsync(string sourceName, string name, CancellationToken cancellationToken = default)
    {
        var match = FindSkill(sourceName, name);
        if (match is null)
        {
            return Task.FromResult<SkillDetailDto?>(null);
        }

        var (source, directory, frontmatter) = match.Value;
        var skillFile = Path.Join(directory, "SKILL.md");
        var content = File.ReadAllText(skillFile);
        var (references, scripts) = ExtractSections(content);

        return Task.FromResult<SkillDetailDto?>(new SkillDetailDto(
            frontmatter.Name,
            frontmatter.Description,
            source.Source,
            directory,
            frontmatter.Tools,
            references,
            scripts,
            content,
            ListFiles(directory)));
    }

    public Task<SkillFileResult> GetFileAsync(string sourceName, string name, string relativePath, CancellationToken cancellationToken = default)
    {
        var match = FindSkill(sourceName, name);
        if (match is null)
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.NotFound));
        }

        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\')
            || relativePath.Contains('%')
            || Path.IsPathRooted(relativePath)
            || relativePath.Split('/').Any(segment => segment is "" or ".."))
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.InvalidPath));
        }

        if (relativePath.Split('/').Any(segment => segment.StartsWith('.')))
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.NotFound));
        }

        var root = Path.GetFullPath(match.Value.Directory);
        if (!TryResolveInsideRoot(root, relativePath, out var file))
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.NotFound));
        }

        if (!IsTextFile(relativePath))
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.NotText));
        }

        if (file.Length > MaxFileBytes)
        {
            return Task.FromResult(SkillFileResult.Fail(SkillFileError.TooLarge));
        }

        var normalized = relativePath.Replace('\\', '/');
        return Task.FromResult(SkillFileResult.Ok(normalized, File.ReadAllText(file.FullName)));
    }

    private (SkillDiscoverySource Source, string Directory, Frontmatter Frontmatter)? FindSkill(string sourceName, string name)
    {
        var source = _sources.FirstOrDefault(s => s.Source.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
        if (source is null || !Directory.Exists(source.Path))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(source.Path))
        {
            var skillFile = Path.Join(directory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                continue;
            }

            var frontmatter = FrontmatterReader.Read(skillFile);
            if (frontmatter is null || !frontmatter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (source, directory, frontmatter);
        }

        return null;
    }

    private static IReadOnlyList<SkillFileDto> ListFiles(string skillDirectory)
    {
        var root = Path.GetFullPath(skillDirectory);
        var files = new List<SkillFileDto>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (files.Count >= MaxFilesPerSkill)
            {
                break;
            }

            var relative = Path.GetRelativePath(root, path);
            if (HasHiddenSegment(relative))
            {
                continue;
            }

            if (!TryResolveInsideRoot(root, relative, out var file))
            {
                continue;
            }

            var normalized = relative.Replace(Path.DirectorySeparatorChar, '/');
            files.Add(new SkillFileDto(normalized, file.Length, IsTextFile(normalized)));
        }

        files.Sort(static (a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.Ordinal));
        return files.AsReadOnly();
    }

    private static bool HasHiddenSegment(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.StartsWith('.'));

    private static bool TryResolveInsideRoot(string root, string relativePath, out FileInfo file)
    {
        var fullPath = Path.GetFullPath(Path.Join(root, relativePath));
        file = new FileInfo(fullPath);

        if (!file.Exists || !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        // Resolve every path segment: a symlinked file OR directory escaping the root is rejected.
        var resolved = root;
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var candidate = Path.Join(resolved, segment);
            var target = new FileInfo(candidate).LinkTarget ?? new DirectoryInfo(candidate).LinkTarget;
            resolved = target is null
                ? candidate
                : Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Join(resolved, target));

            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }
        }

        file = new FileInfo(resolved);
        return file.Exists;
    }

    private static bool IsTextFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(relativePath);
        return extension.Length == 0 || TextExtensions.Contains(extension);
    }

    private static (string? References, string? Scripts) ExtractSections(string content)
    {
        var referencesHeading = "## References";
        var scriptsHeading = "## Scripts";

        var references = ExtractSection(content, referencesHeading);
        var scripts = ExtractSection(content, scriptsHeading);

        return (references, scripts);
    }

    private static string? ExtractSection(string content, string heading)
    {
        var index = content.IndexOf(heading, StringComparison.Ordinal);
        if (index == -1)
        {
            return null;
        }

        var start = index + heading.Length;
        var end = content.Length;

        for (var i = start; i < content.Length; i++)
        {
            if (i + 1 < content.Length && content[i] == '\r' && content[i + 1] == '\n')
            {
                continue;
            }

            if (content[i] == '#' && (i == start || content[i - 1] == '\n'))
            {
                end = i;
                break;
            }
        }

        var value = content[start..end].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
