using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Integrations.Workspace;
using Taskboard.Workspace;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// <see cref="IRepositoryProvisioningService"/> over <see cref="IGitCommandRunner"/> —
/// reuses <c>&lt;root&gt;/&lt;name&gt;</c> when it is a clone of the requested
/// repo, clones from GitHub when missing/empty, and refuses foreign or
/// non-repo directories instead of silently falling back to the workspace
/// root (SPEC-20260923-cockpit-run-hardening RF-001).
/// </summary>
public sealed class RepositoryProvisioningService : IRepositoryProvisioningService
{
    private static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly IGitCommandRunner _git;
    private readonly WorkspaceService _workspace;
    private readonly ILogger<RepositoryProvisioningService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public RepositoryProvisioningService(
        IGitCommandRunner git,
        WorkspaceService workspace,
        ILogger<RepositoryProvisioningService> logger)
    {
        _git = git;
        _workspace = workspace;
        _logger = logger;
    }

    public string ResolveClonePath(string repositoryFullName)
    {
        var name = WorkspacePaths.SanitizeRepoName(repositoryFullName);
        if (name.Length == 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Repository '{repositoryFullName}' is not a valid 'owner/name'.");
        }

        var path = Path.Combine(_workspace.EnsureRoot(), name);
        if (!WorkspacePaths.IsUnder(_workspace.Root, path))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.RepositoryProvisioningFailed,
                $"Clone path '{path}' escapes the workspace root.");
        }

        return path;
    }

    public async Task<RepositoryCloneResult> EnsureCloneAsync(
        string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var path = ResolveClonePath(repositoryFullName);

        // Concurrent starts of the same repo share one gate — a second caller
        // re-checks after the first finishes and reuses the clone.
        var gate = _gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                await VerifyCloneIdentityAsync(path, repositoryFullName, cancellationToken)
                    .ConfigureAwait(false);
                return new RepositoryCloneResult(path, Cloned: false, stopwatch.ElapsedMilliseconds);
            }

            var url = $"https://github.com/{repositoryFullName.Trim()}.git";
            _logger.LogInformation("Cloning {Repo} into {Path}…", repositoryFullName, path);
            var clone = await _git.RunAsync(
                _workspace.EnsureRoot(),
                ["clone", url, path],
                CloneTimeout,
                cancellationToken).ConfigureAwait(false);
            if (clone.TimedOut || clone.ExitCode != 0)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.RepositoryProvisioningFailed,
                    $"git clone {url} failed{(clone.TimedOut ? " (timeout)" : "")}: {clone.StandardError.Trim()}");
            }

            _logger.LogInformation("Cloned {Repo} into {Path} in {Elapsed}ms.",
                repositoryFullName, path, stopwatch.ElapsedMilliseconds);
            return new RepositoryCloneResult(path, Cloned: true, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The existing directory must be a git repo whose <c>origin</c> points at
    /// the requested <c>owner/name</c> — anything else is a conflict, never a
    /// silent reuse or deletion.
    /// </summary>
    private async Task VerifyCloneIdentityAsync(
        string path, string repositoryFullName, CancellationToken cancellationToken)
    {
        var probe = await _git.RunAsync(
            path, ["rev-parse", "--is-inside-work-tree"], ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (probe.ExitCode != 0
            || !string.Equals(probe.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.RepositoryProvisioningFailed,
                $"'{path}' exists but is not a git repository — remove it or pick another name.");
        }

        var remote = await _git.RunAsync(
            path, ["remote", "get-url", "origin"], ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);
        var url = remote.ExitCode == 0 ? remote.StandardOutput.Trim() : string.Empty;
        if (!RemoteMatches(url, repositoryFullName))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.RepositoryProvisioningFailed,
                $"'{path}' is a clone of '{url}', not '{repositoryFullName}' — refusing to reuse it.");
        }
    }

    /// <summary>
    /// Matches <c>https://github.com/o/n(.git)</c>, <c>git@github.com:o/n(.git)</c>
    /// and <c>ssh://git@github.com/o/n(.git)</c> — case-insensitive.
    /// </summary>
    internal static bool RemoteMatches(string remoteUrl, string repositoryFullName)
    {
        var expected = repositoryFullName.Trim().TrimEnd('/');
        if (expected.Length == 0 || string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        var url = remoteUrl.Trim();
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        // Normalize "git@github.com:owner/name" → "github.com/owner/name".
        var schemeAt = url.IndexOf('@');
        var colon = url.IndexOf(':');
        if (schemeAt >= 0 && colon > schemeAt)
        {
            url = string.Concat(url.AsSpan(schemeAt + 1, colon - schemeAt - 1), "/", url.AsSpan(colon + 1));
        }

        return url.EndsWith($"/{expected}", StringComparison.OrdinalIgnoreCase);
    }
}
