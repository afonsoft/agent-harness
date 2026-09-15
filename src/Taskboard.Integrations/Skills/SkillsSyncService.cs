using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Skills;

namespace Taskboard.Integrations.Skills;

/// <summary>
/// Clones/updates the configured skills repository into a local cache and
/// installs every valid skill under <c>skills/*</c> into the skills
/// directories of the enabled agent CLIs (SPEC-20260915-skills-repo-sync).
/// Synchronization is additive: files not managed by a sync run are never
/// removed from the destination.
/// </summary>
public sealed class SkillsSyncService : ISkillsSyncService
{
    internal const string RepositoryConfigKey = "Taskboard:Skills:Repository";
    internal const string RepositoryEnvAlias = "TASKBOARD_SKILLS_REPO";
    internal const string DefaultRepository = "afonsoft/skills";
    internal const string ManifestFileName = ".taskboard-skills.json";
    internal const int DefaultTimeoutSeconds = 120;

    private static readonly Regex OwnerRepoPattern =
        new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;
    private readonly ILogger<SkillsSyncService> _logger;
    private readonly string _cacheDirectory;
    private readonly string _homeDirectory;
    private readonly Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> _enabledAgentsProvider;
    private readonly Func<CancellationToken, Task<string?>>? _accessTokenProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile SkillsSyncStatus _status = SkillsSyncStatus.Empty;

    private sealed record SourceSkill(string Name, string Directory, string Hash);

    public SkillsSyncService(
        IConfiguration configuration,
        ILogger<SkillsSyncService> logger,
        string cacheDirectory,
        string homeDirectory,
        Func<CancellationToken, Task<IReadOnlyCollection<AgentType>>> enabledAgentsProvider,
        Func<CancellationToken, Task<string?>>? accessTokenProvider = null)
    {
        _configuration = configuration;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        _homeDirectory = homeDirectory;
        _enabledAgentsProvider = enabledAgentsProvider;
        _accessTokenProvider = accessTokenProvider;
    }

    public SkillsSyncStatus GetStatus() => _status;

    public void RequestSync(IReadOnlyCollection<AgentType>? agents = null)
    {
        if (!_gate.Wait(0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SyncCoreAsync(agents, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public async Task<SkillsSyncStatus> SyncAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
            return await SyncCoreAsync(agents, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SkillsSyncStatus> SyncCoreAsync(
        IReadOnlyCollection<AgentType>? agents,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        _status = _status with { State = SkillsSyncState.Running, Error = null };

        var repository = string.Empty;
        try
        {
            repository = ResolveRepository();
            var targets = agents ?? await _enabledAgentsProvider(cancellationToken).ConfigureAwait(false);
            var token = _accessTokenProvider is null
                ? null
                : await _accessTokenProvider(cancellationToken).ConfigureAwait(false);

            var timeout = GetTimeout();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            await EnsureCacheAsync(repository, token, timeoutSource.Token).ConfigureAwait(false);
            var skills = LoadSourceSkills();

            var results = new List<AgentSyncResult>();
            foreach (var agent in targets)
            {
                try
                {
                    results.Add(SyncAgent(agent, skills, repository));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skills sync failed for agent {AgentType}.", agent);
                    results.Add(new AgentSyncResult(agent.ToString(), 0, 0, 0, ex.Message));
                }
            }

            var state = results.All(r => r.Error is null)
                ? SkillsSyncState.Succeeded
                : SkillsSyncState.Failed;

            _status = new SkillsSyncStatus(
                state,
                started,
                stopwatch.ElapsedMilliseconds,
                repository,
                state == SkillsSyncState.Failed ? "One or more agents failed to synchronize." : null,
                results);
            return _status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skills sync failed for repository {Repository}.", repository);
            _status = new SkillsSyncStatus(
                SkillsSyncState.Failed,
                started,
                stopwatch.ElapsedMilliseconds,
                repository,
                ex.Message,
                []);
            return _status;
        }
    }

    internal string ResolveRepository()
    {
        if (_configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers)
            {
                if (provider is IOverrideConfigurationProvider overrides
                    && overrides.TryGetOverride(RepositoryConfigKey, out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        var env = Environment.GetEnvironmentVariable(RepositoryEnvAlias);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var configured = _configuration[RepositoryConfigKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultRepository : configured.Trim();
    }

    /// <summary>
    /// Normalizes the configured repository to a clonable URL:
    /// <c>owner/repo</c> becomes <c>https://github.com/owner/repo.git</c>;
    /// absolute URLs and local paths pass through.
    /// </summary>
    internal static string NormalizeRepoUrl(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("git@", StringComparison.Ordinal)
            || Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        if (OwnerRepoPattern.IsMatch(trimmed))
        {
            return $"https://github.com/{trimmed}.git";
        }

        throw new ArgumentException(
            $"Invalid skills repository '{value}'. Expected 'owner/repo' or an absolute git URL.");
    }

    private async Task EnsureCacheAsync(string repository, string? token, CancellationToken cancellationToken)
    {
        var url = NormalizeRepoUrl(repository);
        var gitDirectory = Path.Join(_cacheDirectory, ".git");

        if (Directory.Exists(gitDirectory))
        {
            var remote = await GitRunner
                .RunAsync(_cacheDirectory, null, cancellationToken, "remote", "get-url", "origin")
                .ConfigureAwait(false);
            if (remote.ExitCode != 0
                || !remote.StdOut.Trim().Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Skills cache remote changed from {OldRemote} to {NewRemote}; re-cloning.",
                    remote.StdOut.Trim(),
                    url);
                Directory.Delete(_cacheDirectory, recursive: true);
            }
        }
        else if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }

        if (!Directory.Exists(gitDirectory))
        {
            var parent = Path.GetDirectoryName(_cacheDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var clone = await GitRunner
                .RunAsync(parent ?? ".", token, cancellationToken, "clone", "--depth", "1", url, _cacheDirectory)
                .ConfigureAwait(false);
            if (clone.ExitCode != 0)
            {
                throw new InvalidOperationException($"git clone failed: {clone.StdErr.Trim()}");
            }
        }
        else
        {
            var fetch = await GitRunner
                .RunAsync(_cacheDirectory, token, cancellationToken, "fetch", "--depth", "1", "origin")
                .ConfigureAwait(false);
            if (fetch.ExitCode != 0)
            {
                throw new InvalidOperationException($"git fetch failed: {fetch.StdErr.Trim()}");
            }

            var reset = await GitRunner
                .RunAsync(_cacheDirectory, null, cancellationToken, "reset", "--hard", "FETCH_HEAD")
                .ConfigureAwait(false);
            if (reset.ExitCode != 0)
            {
                throw new InvalidOperationException($"git reset failed: {reset.StdErr.Trim()}");
            }
        }
    }

    private List<SourceSkill> LoadSourceSkills()
    {
        var skillsRoot = Path.Join(_cacheDirectory, "skills");
        var skills = new List<SourceSkill>();
        if (!Directory.Exists(skillsRoot))
        {
            _logger.LogWarning("Skills repository has no 'skills/' directory.");
            return skills;
        }

        foreach (var directory in Directory.EnumerateDirectories(skillsRoot))
        {
            var skillFile = Path.Join(directory, "SKILL.md");
            if (!File.Exists(skillFile) || FrontmatterReader.Read(skillFile) is null)
            {
                _logger.LogWarning("Skipping '{Directory}': missing or invalid SKILL.md.", directory);
                continue;
            }

            skills.Add(new SourceSkill(
                Path.GetFileName(directory),
                directory,
                ComputeSkillHash(directory)));
        }

        return skills;
    }

    private AgentSyncResult SyncAgent(AgentType agent, List<SourceSkill> skills, string repository)
    {
        var installed = 0;
        var updated = 0;
        var skipped = 0;

        var sourceNames = skills.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var targetRoot in AgentSkillDirectoryMap.GetSkillDirectories(agent, _homeDirectory))
        {
            Directory.CreateDirectory(targetRoot);
            var manifestPath = Path.Join(targetRoot, ManifestFileName);
            var manifest = SkillsManifest.Load(manifestPath);

            foreach (var name in manifest.Skills.Keys.ToList())
            {
                manifest.Skills[name] = manifest.Skills[name] with
                {
                    RemovedFromSource = !sourceNames.Contains(name)
                };
            }

            foreach (var skill in skills)
            {
                var targetDirectory = Path.Join(targetRoot, skill.Name);
                var exists = manifest.Skills.TryGetValue(skill.Name, out var entry);
                if (exists && entry!.Hash == skill.Hash && Directory.Exists(targetDirectory))
                {
                    skipped++;
                    continue;
                }

                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, recursive: true);
                }

                CopyDirectory(skill.Directory, targetDirectory);
                manifest.Skills[skill.Name] = new SkillsManifestEntry(
                    skill.Hash, DateTimeOffset.UtcNow, repository, RemovedFromSource: false);
                if (exists)
                {
                    updated++;
                }
                else
                {
                    installed++;
                }
            }

            manifest.Save(manifestPath);
        }

        return new AgentSyncResult(agent.ToString(), installed, updated, skipped, null);
    }

    internal static string ComputeSkillHash(string skillDirectory)
    {
        using var sha256 = SHA256.Create();
        var root = Path.GetFullPath(skillDirectory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var relativePath in files)
        {
            var nameBytes = Encoding.UTF8.GetBytes(relativePath + "\n");
            sha256.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            var content = File.ReadAllBytes(Path.Join(root, relativePath));
            sha256.TransformBlock(content, 0, content.Length, null, 0);
        }

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Join(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Join(destination, Path.GetFileName(directory)));
        }
    }

    private TimeSpan GetTimeout()
    {
        var configured = _configuration["Taskboard:Skills:SyncTimeoutSeconds"];
        return int.TryParse(configured, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }
}
