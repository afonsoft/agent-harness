using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.Integrations.Skills;

/// <summary>Executes a git invocation; matches <see cref="GitRunner.RunAsync"/>.</summary>
internal delegate Task<GitResult> GitExecutor(
    string workingDirectory,
    string? authToken,
    CancellationToken cancellationToken,
    params string[] args);

/// <summary>Outcome of the cache accessibility pre-check (SPEC-20260918-skills-cache-permissions).</summary>
internal enum CachePrepareResult
{
    /// <summary>The cache directory was missing or already usable.</summary>
    Healthy,

    /// <summary>An inaccessible cache was moved aside; a fresh clone is required.</summary>
    Recovered
}

/// <summary>
/// Shared resolution and cache management for the configured skills
/// repository (SPEC-20260915-skills-repo-sync, SPEC-20260917-skills-installer).
/// </summary>
internal static class SkillsRepository
{
    internal const string ConfigKey = "Taskboard:Skills:Repository";
    internal const string EnvAlias = "TASKBOARD_SKILLS_REPO";
    internal const string DefaultRepository = "afonsoft/skills";

    private static readonly Regex OwnerRepoPattern =
        new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Resolves the configured repository: database override &gt; env alias &gt;
    /// configuration value &gt; default.
    /// </summary>
    internal static string Resolve(IConfiguration configuration)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers)
            {
                if (provider is IOverrideConfigurationProvider overrides
                    && overrides.TryGetOverride(ConfigKey, out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        var env = Environment.GetEnvironmentVariable(EnvAlias);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var configured = configuration[ConfigKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultRepository : configured.Trim();
    }

    /// <summary>
    /// Normalizes the configured repository to a clonable URL:
    /// <c>owner/repo</c> becomes <c>https://github.com/owner/repo.git</c>;
    /// absolute URLs and local paths pass through.
    /// </summary>
    internal static string NormalizeUrl(string value)
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

    /// <summary>
    /// Ensures the cache directory is usable by the current process. A cache
    /// left behind by another user (e.g. a previous run as root) is renamed
    /// aside — never deleted — so a clean clone can take its place. Renaming
    /// only requires write access on the parent directory, which belongs to
    /// the service user. Stale <c>.inaccessible-*</c> directories are kept for
    /// manual cleanup (SPEC-20260918-skills-cache-permissions).
    /// </summary>
    internal static CachePrepareResult EnsureAccessible(string cacheDirectory, ILogger logger)
    {
        if (!Directory.Exists(cacheDirectory) || IsUsable(cacheDirectory))
        {
            return CachePrepareResult.Healthy;
        }

        var quarantine = $"{cacheDirectory}.inaccessible-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        try
        {
            Directory.Move(cacheDirectory, quarantine);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Skills cache '{cacheDirectory}' is owned by another user and could not be moved aside. " +
                $"Remove it or fix ownership manually (e.g. sudo chown -R {Environment.UserName} '{cacheDirectory}').",
                ex);
        }

        logger.LogWarning(
            "Skills cache {CacheDirectory} was inaccessible — moved to {Quarantine} for a fresh clone. " +
            "The stale directory is kept for manual inspection/cleanup.",
            cacheDirectory,
            quarantine);
        return CachePrepareResult.Recovered;
    }

    /// <summary>
    /// Probes whether every file and subdirectory of the cache is readable and
    /// writable by the current process. A foreign-owned tree (e.g. root-owned
    /// files) fails the probe even when the top-level directory is writable,
    /// because git checkout/reset rewrites tracked files.
    /// </summary>
    private static bool IsUsable(string cacheDirectory)
    {
        try
        {
            // Enumerating throws on any unreadable subdirectory; opening each
            // file for writing covers git fetch/reset and script chmod.
            foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }

            return CanWriteInside(cacheDirectory);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CanWriteInside(string directory)
    {
        var probe = Path.Join(directory, $".access-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            try
            {
                File.Delete(probe);
            }
            catch
            {
                // Probe file could not be created or removed — directory is not writable.
            }

            return false;
        }
    }

    /// <summary>
    /// Ensures every <c>*.sh</c> in the cache is executable — a legacy cache
    /// may have lost the bit even though a fresh clone preserves it.
    /// Idempotent: only touches files missing an execute bit.
    /// </summary>
    private static void EnsureScriptsExecutable(string cacheDirectory, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var script in Directory.EnumerateFiles(cacheDirectory, "*.sh", SearchOption.AllDirectories))
        {
            try
            {
                var mode = File.GetUnixFileMode(script);
                var executable = mode
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute;
                if (mode != executable)
                {
                    File.SetUnixFileMode(script, executable);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not mark {Script} executable.", script);
            }
        }
    }

    /// <summary>
    /// Ensures <paramref name="cacheDirectory"/> holds an up-to-date clone of
    /// <paramref name="repository"/>. A cached clone whose origin differs is
    /// deleted and re-cloned. Returns the accessibility pre-check outcome so
    /// callers can report a recovered cache (SPEC-20260918-skills-cache-permissions).
    /// </summary>
    internal static async Task<CachePrepareResult> EnsureCacheAsync(
        string cacheDirectory,
        string repository,
        string? token,
        GitExecutor git,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var prepare = EnsureAccessible(cacheDirectory, logger);
        var url = NormalizeUrl(repository);
        var gitDirectory = Path.Join(cacheDirectory, ".git");

        if (Directory.Exists(gitDirectory))
        {
            var remote = await git(cacheDirectory, null, cancellationToken, "remote", "get-url", "origin")
                .ConfigureAwait(false);
            if (remote.ExitCode != 0
                || !remote.StdOut.Trim().Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Skills cache remote changed from {OldRemote} to {NewRemote}; re-cloning.",
                    remote.StdOut.Trim(),
                    url);
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
        else if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }

        if (!Directory.Exists(gitDirectory))
        {
            var parent = Path.GetDirectoryName(cacheDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var clone = await git(parent ?? ".", token, cancellationToken, "clone", "--depth", "1", url, cacheDirectory)
                .ConfigureAwait(false);
            if (clone.ExitCode != 0)
            {
                throw new InvalidOperationException($"git clone failed: {clone.StdErr.Trim()}");
            }
        }
        else
        {
            var fetch = await git(cacheDirectory, token, cancellationToken, "fetch", "--depth", "1", "origin")
                .ConfigureAwait(false);
            if (fetch.ExitCode != 0)
            {
                throw new InvalidOperationException($"git fetch failed: {fetch.StdErr.Trim()}");
            }

            var reset = await git(cacheDirectory, null, cancellationToken, "reset", "--hard", "FETCH_HEAD")
                .ConfigureAwait(false);
            if (reset.ExitCode != 0)
            {
                throw new InvalidOperationException($"git reset failed: {reset.StdErr.Trim()}");
            }
        }

        EnsureScriptsExecutable(cacheDirectory, logger);
        return prepare;
    }
}
