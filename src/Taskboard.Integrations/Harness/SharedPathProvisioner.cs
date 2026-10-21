using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-002/RF-004/RF-005 (P1): resolves
/// gitignored paths that must exist inside a new worktree and materializes
/// them. P1 sources: <c>.worktreeinclude</c> (gitignore-style, <c>!</c>
/// negation = exclude) + convention auto-detect. Modes: <c>copy</c>
/// (<c>cp -a</c> — preserves exec bits and inner symlinks), <c>symlink</c>
/// (relative), <c>skip</c>. Never overwrites an existing worktree path and
/// never throws on a single bad path — the entry is marked skipped.
/// </summary>
public sealed class SharedPathProvisioner : ISharedPathProvisioner
{
    /// <summary>NFR: at most 50 shared paths per worktree.</summary>
    internal const int MaxSharedPaths = 50;

    private const string IncludeFileName = ".worktreeinclude";

    /// <summary>RF-002: config sections — <c>Taskboard:Worktree:SharedPaths</c>
    /// entries (<c>{path, mode?}</c> or scalar) and
    /// <c>Taskboard:Worktree:SharedExclude</c> scalars.</summary>
    private const string ConfigSharedSection = "Taskboard:Worktree:SharedPaths";

    private const string ConfigExcludeSection = "Taskboard:Worktree:SharedExclude";
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Convention table (open question 2): <c>.env</c> resolves as
    /// <c>skip</c> — present in the listing, never copied unless the user
    /// opts in via a higher-precedence source.
    /// </summary>
    private static readonly IReadOnlyList<(string Path, string Mode)> AutoDetect =
    [
        ("node_modules", WorktreeSharedModes.Copy),
        (".env", WorktreeSharedModes.Skip),
        ("venv", WorktreeSharedModes.Copy),
        (".venv", WorktreeSharedModes.Copy),
        ("bin", WorktreeSharedModes.Copy),
        ("obj", WorktreeSharedModes.Copy),
        (".next", WorktreeSharedModes.Copy),
    ];

    private readonly IProcessRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SharedPathProvisioner> _logger;

    public SharedPathProvisioner(
        IProcessRunner runner,
        IConfiguration configuration,
        ILogger<SharedPathProvisioner> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SharedPathEntryDto>> ProvisionAsync(
        string repositoryPath,
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        var resolved = Resolve(
            repositoryPath,
            ReadConfigDeclarations(_configuration),
            ReadConfigExcludes(_configuration),
            _logger);
        var results = new List<SharedPathEntryDto>(resolved.Count);
        foreach (var entry in resolved)
        {
            results.Add(await ApplyAsync(
                repositoryPath, worktreePath, entry, cancellationToken)
                .ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Merge declaration sources in precedence order — config &gt; repoYaml
    /// &gt; include &gt; auto (RF-002). Every exclude (config list, yaml
    /// <c>exclude:</c>, include <c>!</c> negation) beats every declaration.
    /// First declaration of a path wins; duplicates never apply twice.
    /// </summary>
    internal static List<WorktreeSharedResolution> Resolve(
        string repositoryPath,
        IReadOnlyList<WorktreeSharedResolution>? configEntries = null,
        IReadOnlyCollection<string>? configExcludes = null,
        ILogger? logger = null)
    {
        var declared = new List<WorktreeSharedResolution>(configEntries ?? []);
        var excluded = new HashSet<string>(
            configExcludes ?? [], StringComparer.Ordinal);

        var (yamlShared, yamlExclude) = ReadRepoYaml(repositoryPath, logger);
        declared.AddRange(yamlShared);
        excluded.UnionWith(yamlExclude);

        foreach (var line in ReadIncludeFile(repositoryPath))
        {
            if (line.StartsWith('!'))
            {
                if (Normalize(line[1..]) is { } neg)
                {
                    excluded.Add(neg);
                }

                continue;
            }

            if (Normalize(line) is { } path)
            {
                // A rooted or traversing raw line normalizes to a benign
                // relative path — keep the marker so Apply rejects it.
                var invalid = Path.IsPathRooted(line)
                    || path.Split('/').Any(s => s == "..");
                declared.Add(new WorktreeSharedResolution(
                    path, WorktreeSharedModes.Copy,
                    WorktreeSharedSources.Include, invalid));
            }
        }

        // Excludes beat every source — including config declarations.
        declared.RemoveAll(e => excluded.Contains(e.Path));

        // First declaration wins — dedup preserves precedence order.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        declared.RemoveAll(e => !seen.Add(e.Path));

        foreach (var (path, mode) in AutoDetect)
        {
            if (seen.Contains(path) || excluded.Contains(path))
            {
                continue;
            }

            if (Path.Exists(Path.Join(repositoryPath, path)))
            {
                declared.Add(new WorktreeSharedResolution(
                    path, mode, WorktreeSharedSources.Auto, Invalid: false));
                seen.Add(path);
            }
        }

        return declared.Count <= MaxSharedPaths
            ? declared
            : declared.Take(MaxSharedPaths).ToList();
    }

    /// <summary>
    /// RF-003: parses <c>.harness/worktree.yaml</c> via the shared
    /// <see cref="WorktreeYamlReader"/> (SPEC-20261021) —
    /// <c>shared: [{path, mode?} | scalar]</c> + <c>exclude: [path]</c>.
    /// Malformed yaml logs a warning and contributes nothing.
    /// </summary>
    internal static (List<WorktreeSharedResolution> Shared, List<string> Exclude)
        ReadRepoYaml(string repositoryPath, ILogger? logger = null)
    {
        var doc = WorktreeYamlReader.Parse(repositoryPath, logger);
        var shared = doc.Shared
            .Select(e => new WorktreeSharedResolution(
                e.Path,
                WorktreeSharedModes.IsValid(e.Mode ?? "")
                    ? e.Mode!
                    : WorktreeSharedModes.Copy,
                WorktreeSharedSources.RepoYaml,
                e.Invalid))
            .ToList();
        return (shared, doc.Exclude.ToList());
    }

    /// <summary>Reads <c>Taskboard:Worktree:SharedPaths</c> — each child is
    /// either a scalar (<c>SharedPaths__0=path</c>) or a map
    /// (<c>SharedPaths__0__path</c> + optional <c>__mode</c>).</summary>
    private static List<WorktreeSharedResolution> ReadConfigDeclarations(
        IConfiguration configuration)
    {
        var entries = new List<WorktreeSharedResolution>();
        foreach (var child in configuration.GetSection(ConfigSharedSection).GetChildren())
        {
            var raw = child["path"] ?? child.Value;
            if (raw is null || Normalize(raw) is not { } path)
            {
                continue;
            }

            var mode = child["mode"];
            var invalid = Path.IsPathRooted(raw)
                || path.Split('/').Any(s => s == "..");
            entries.Add(new WorktreeSharedResolution(
                path,
                WorktreeSharedModes.IsValid(mode ?? "")
                    ? mode!
                    : WorktreeSharedModes.Copy,
                WorktreeSharedSources.Config,
                invalid));
        }

        return entries;
    }

    /// <summary>Reads <c>Taskboard:Worktree:SharedExclude</c> scalars.</summary>
    private static List<string> ReadConfigExcludes(IConfiguration configuration) =>
        configuration.GetSection(ConfigExcludeSection).GetChildren()
            .Select(c => c.Value)
            .Where(v => v is not null)
            .Select(v => Normalize(v!))
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();

    /// <summary>Reads <c>.worktreeinclude</c> — blank lines and <c>#</c>
    /// comments dropped; missing file yields no entries.</summary>
    internal static IEnumerable<string> ReadIncludeFile(string repositoryPath)
    {
        var file = Path.Join(repositoryPath, IncludeFileName);
        if (!File.Exists(file))
        {
            yield break;
        }

        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// Materializes one entry. copy → <c>cp -a src dst</c> (preserves mode
    /// bits + inner symlinks); symlink → relative link; skip/missing/
    /// invalid/existing never touch the worktree. Internal: tests drive
    /// symlink entries directly — no P1 file source declares a mode
    /// (symlink becomes user-reachable via yaml/config in P2).
    /// </summary>
    internal async Task<SharedPathEntryDto> ApplyAsync(
        string repositoryPath, string worktreePath,
        WorktreeSharedResolution entry, CancellationToken ct)
    {
        if (entry.Invalid
            || !TryResolveSource(repositoryPath, entry.Path, out var source))
        {
            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, false, "invalid_shared_path");
        }

        if (entry.Mode == WorktreeSharedModes.Skip)
        {
            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, false, "declared-skip");
        }

        var target = Path.Join(worktreePath, entry.Path);
        if (!Path.Exists(source))
        {
            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, false, "missing");
        }

        if (Path.Exists(target))
        {
            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, false, "exists");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (entry.Mode == WorktreeSharedModes.Symlink)
            {
                var relative = Path.GetRelativePath(
                    Path.GetDirectoryName(target)!, source);
                if (Directory.Exists(source))
                {
                    Directory.CreateSymbolicLink(target, relative);
                }
                else
                {
                    File.CreateSymbolicLink(target, relative);
                }

                return new SharedPathEntryDto(
                    entry.Path, entry.Mode, entry.Source, true, null);
            }

            // `cp -a` — preserves perms/symlinks; reflink CoW where the fs
            // supports it, plain copy otherwise (BSD cp has no --reflink).
            var result = await _runner.RunAsync(
                "cp", worktreePath,
                ["-a", "--reflink=auto", "--", source, target],
                CopyTimeout, ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                result = await _runner.RunAsync(
                    "cp", worktreePath, ["-a", "--", source, target],
                    CopyTimeout, ct).ConfigureAwait(false);
            }

            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Shared path copy {Path} failed: {Err}",
                    entry.Path, result.StdErr.Trim());
                return new SharedPathEntryDto(
                    entry.Path, entry.Mode, entry.Source, false, "copy-failed");
            }

            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Shared path {Path} failed.", entry.Path);
            return new SharedPathEntryDto(
                entry.Path, entry.Mode, entry.Source, false, "io-error");
        }
    }

    /// <summary>
    /// Rejects traversal — empty, rooted, <c>..</c> segments, or anything
    /// resolving outside <paramref name="repositoryPath"/>.
    /// </summary>
    internal static bool TryResolveSource(
        string repositoryPath, string path, out string fullPath)
    {
        fullPath = string.Empty;
        // Rooted check must run on the RAW value — Normalize trims '/'
        // and would otherwise turn "/etc/passwd" into "etc/passwd".
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path.Trim())
            || path.TrimStart().StartsWith("~/", StringComparison.Ordinal)
            || Normalize(path) is not { } normalized
            || normalized.Split('/').Any(s => s == ".."))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Join(repositoryPath, normalized));
        if (!candidate.StartsWith(
                Path.GetFullPath(repositoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static string? Normalize(string raw)
    {
        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        return trimmed.Length == 0 ? null : trimmed;
    }
}

/// <summary>Resolution record before application — path/mode/source +
/// an <c>Invalid</c> marker for declarations that were rooted or
/// traversing in raw form (Normalize hides that signal).</summary>
internal sealed record WorktreeSharedResolution(
    string Path, string Mode, string Source, bool Invalid);
