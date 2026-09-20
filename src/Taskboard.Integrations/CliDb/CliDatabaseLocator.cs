using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb;

/// <summary>
/// Resolves <see cref="CliDbSource"/> patterns to absolute paths under
/// <c>$HOME</c>. Glob patterns expand via <see cref="Directory.GetFiles"/>;
/// nothing is ever created — resolution is pure inspection.
/// SPEC-20260919-cli-db-reader RF-001/RF-006.
/// </summary>
public sealed class CliDatabaseLocator : ICliDatabaseLocator
{
    private readonly string _homeDirectory;
    private readonly ILogger<CliDatabaseLocator> _logger;

    public CliDatabaseLocator(string homeDirectory, ILogger<CliDatabaseLocator> logger)
    {
        _homeDirectory = Path.GetFullPath(homeDirectory);
        _logger = logger;
    }

    public IReadOnlyList<string> Resolve(CliDbSource source)
    {
        var pattern = source.RelativePathPattern;
        if (Path.IsPathFullyQualified(pattern) || pattern.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("CliDb source {Source} pattern rejected (absolute/traversal): {Pattern}",
                source.Name, pattern);
            return [];
        }

        if (!source.IsGlob)
        {
            var path = Path.GetFullPath(Path.Combine(_homeDirectory, pattern));
            return IsUnderHome(path) && File.Exists(path) ? [path] : [];
        }

        var lastSep = pattern.LastIndexOfAny(new[] { '/', Path.DirectorySeparatorChar });
        var dirPart = lastSep < 0 ? string.Empty : pattern[..lastSep];
        var filePattern = lastSep < 0 ? pattern : pattern[(lastSep + 1)..];
        var dir = Path.GetFullPath(Path.Combine(_homeDirectory, dirPart));

        if (!IsUnderHome(dir) || !Directory.Exists(dir))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(dir, filePattern)
                .Where(IsUnderHome)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "CliDb source {Source} glob failed on {Dir}.", source.Name, dir);
            return [];
        }
    }

    public CliDbFileStat? Stat(string absolutePath)
    {
        var path = Path.GetFullPath(absolutePath);
        if (!IsUnderHome(path) || !File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return new CliDbFileStat(info.LastWriteTimeUtc, info.Length);
    }

    public IReadOnlyList<CliDbSourceStatusDto> GetStatus()
    {
        var results = new List<CliDbSourceStatusDto>();
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            foreach (var source in CliDatabaseMap.SourcesFor(kind))
            {
                var resolved = Resolve(source);
                if (resolved.Count == 0)
                {
                    results.Add(new CliDbSourceStatusDto(
                        kind, source.Name, source.RelativePathPattern, null,
                        CliDbSourceStatus.Missing, null));
                    continue;
                }

                foreach (var path in resolved)
                {
                    results.Add(new CliDbSourceStatusDto(
                        kind, source.Name, source.RelativePathPattern, path,
                        CliDbSourceStatus.Available, null));
                }
            }
        }

        return results;
    }

    private bool IsUnderHome(string path) =>
        path.StartsWith(_homeDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || string.Equals(path, _homeDirectory, StringComparison.Ordinal);
}
