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
    /// Ensures <paramref name="cacheDirectory"/> holds an up-to-date clone of
    /// <paramref name="repository"/>. A cached clone whose origin differs is
    /// deleted and re-cloned.
    /// </summary>
    internal static async Task EnsureCacheAsync(
        string cacheDirectory,
        string repository,
        string? token,
        GitExecutor git,
        ILogger logger,
        CancellationToken cancellationToken)
    {
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
    }
}
