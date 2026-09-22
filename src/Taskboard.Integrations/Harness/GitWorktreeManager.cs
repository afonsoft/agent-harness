using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// <see cref="IWorkspaceIsolationService"/> backed by native <c>git worktree</c>
/// over <see cref="IGitCommandRunner"/> — sessions persisted via
/// <see cref="IWorktreeSessionRepository"/> (SPEC-20260919-harness-workspace-isolation).
/// </summary>
public sealed class GitWorktreeManager : IWorkspaceIsolationService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Cap de entradas por diretório no explorer (RF-003).</summary>
    internal const int MaxEntriesPerDirectory = 500;

    /// <summary>Cap de leitura de arquivo no explorer — 512 KB.</summary>
    internal const int MaxContentBytes = 512 * 1024;

    /// <summary>Janela de sniff para detectar binário (NUL nos primeiros 8 KB).</summary>
    internal const int BinarySniffBytes = 8 * 1024;

    private readonly IGitCommandRunner _git;
    private readonly IWorktreeSessionRepository _sessions;
    private readonly string _worktreeRoot;
    private readonly ILogger<GitWorktreeManager> _logger;

    public GitWorktreeManager(
        IGitCommandRunner git,
        IWorktreeSessionRepository sessions,
        string worktreeRoot,
        ILogger<GitWorktreeManager> logger)
    {
        _git = git;
        _sessions = sessions;
        _worktreeRoot = worktreeRoot;
        _logger = logger;
    }

    public async Task<WorktreeSessionDto> CreateWorktreeAsync(
        string runId,
        string repositoryPath,
        string baseBranch,
        string taskSlug,
        bool retainOnFailure = false,
        CancellationToken cancellationToken = default)
    {
        var existing = await _sessions.GetByRunIdAsync(runId, cancellationToken);
        if (existing is not null && Directory.Exists(existing.Path))
        {
            return existing;
        }

        var path = WorktreePaths.SessionDir(_worktreeRoot, runId);
        if (!WorktreePaths.IsUnder(_worktreeRoot, path))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Worktree path escapes the approved root.");
        }

        if (Directory.Exists(path))
        {
            _logger.LogInformation("Cleaning stale worktree directory {Path}.", path);
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(_worktreeRoot);

        // A base pedida pode não existir no clone (ex.: "main" num repo cujo
        // default é "master") — resolve para o default do remoto ou HEAD, senão
        // o worktree add falha e o run fica preso em Pending sem sinal visível.
        var resolvedBase = await ResolveBaseRefAsync(repositoryPath, baseBranch, cancellationToken);

        var branch = await AddWorktreeWithUniqueBranchAsync(repositoryPath, path, resolvedBase, runId, taskSlug, cancellationToken);

        var session = await _sessions.CreateAsync(
            runId,
            repositoryPath,
            resolvedBase,
            path,
            branch,
            retainOnFailure,
            cancellationToken);

        _logger.LogInformation("Worktree {Path} on branch {Branch} created for run {RunId}.", path, branch, runId);
        return session;
    }

    public async Task<WorktreeSessionDto?> GetAsync(string runId, CancellationToken cancellationToken = default)
        => await _sessions.GetByRunIdAsync(runId, cancellationToken);

    public async Task<WorkspaceDiffDto> GetDiffAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var status = await _git.RunAsync(session.Path, ["status", "--porcelain"], GitTimeout, cancellationToken);
        EnsureSuccess(status, "git status");

        // Two-dot diff vs the base branch covers committed AND working-tree
        // (pending) changes — RF-002 exige ambos.
        var numstat = await _git.RunAsync(session.Path, ["diff", "--numstat", session.BaseBranch], GitTimeout, cancellationToken);
        EnsureSuccess(numstat, "git diff --numstat");

        var patch = await _git.RunAsync(session.Path, ["diff", session.BaseBranch], GitTimeout, cancellationToken);
        EnsureSuccess(patch, "git diff");

        var perFile = ParseNumstatPerFile(numstat.StandardOutput);
        var files = ParseStatus(status.StandardOutput)
            .Select(f => perFile.TryGetValue(f.Path, out var counts)
                ? f with { Insertions = counts.Insertions, Deletions = counts.Deletions }
                : f)
            .ToList();
        var insertions = perFile.Values.Sum(v => v.Insertions);
        var deletions = perFile.Values.Sum(v => v.Deletions);

        return new WorkspaceDiffDto(files.Count, insertions, deletions, files, patch.StandardOutput);
    }

    /// <inheritdoc />
    public async Task<WorktreeListDto?> ListFilesAsync(
        string runId,
        string? subdir,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken).ConfigureAwait(false);
        var directory = ResolveInsideWorktree(session.Path, subdir);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var entries = new List<WorktreeEntryDto>();
        var truncated = false;
        foreach (var fullPath in Directory.EnumerateFileSystemEntries(directory)
                     .OrderBy(p => !System.IO.Directory.Exists(p))
                     .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(fullPath);
            if (IsGitMetadata(info) || EscapesViaLink(info, session.Path))
            {
                continue;
            }

            if (entries.Count >= MaxEntriesPerDirectory)
            {
                truncated = true;
                break;
            }

            var isDirectory = System.IO.Directory.Exists(fullPath);
            entries.Add(new WorktreeEntryDto(
                info.Name,
                RelativePath(session.Path, fullPath),
                isDirectory,
                isDirectory ? null : info.Length));
        }

        var rel = RelativePath(session.Path, directory);
        return new WorktreeListDto(rel, entries, truncated);
    }

    /// <inheritdoc />
    public async Task<WorktreeFileContentDto?> ReadFileAsync(
        string runId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken).ConfigureAwait(false);
        var file = ResolveInsideWorktree(session.Path, path);

        var info = new FileInfo(file);
        if (!info.Exists || System.IO.Directory.Exists(file) || EscapesViaLink(info, session.Path))
        {
            return null;
        }

        var buffer = new byte[Math.Min(info.Length, MaxContentBytes + 1)];
        await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (buffer.AsSpan(0, Math.Min(read, BinarySniffBytes)).IndexOf((byte)0) >= 0)
            {
                return new WorktreeFileContentDto(path, null, info.Length, false, true);
            }

            var truncated = info.Length > MaxContentBytes;
            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, truncated ? MaxContentBytes : read);
            return new WorktreeFileContentDto(path, content, info.Length, truncated, false);
        }
    }

    public async Task<string> CommitAsync(
        string runId,
        string message,
        string author,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var add = await _git.RunAsync(session.Path, ["add", "-A"], GitTimeout, cancellationToken);
        EnsureSuccess(add, "git add");

        var commit = await _git.RunAsync(
            session.Path,
            ["commit", "-m", message, "--author", author],
            GitTimeout,
            cancellationToken);
        EnsureSuccess(commit, "git commit");

        var revParse = await _git.RunAsync(session.Path, ["rev-parse", "HEAD"], GitTimeout, cancellationToken);
        EnsureSuccess(revParse, "git rev-parse");

        var sha = revParse.StandardOutput.Trim();
        await _sessions.RecordCommitAsync(runId, sha, cancellationToken);
        return sha;
    }

    /// <inheritdoc />
    public async Task<string> PushAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);
        var push = await _git.RunAsync(
            session.Path,
            ["push", "-u", "origin", session.Branch],
            GitTimeout,
            cancellationToken);
        EnsureSuccess(push, "git push");
        _logger.LogInformation("Run {RunId}: branch {Branch} pushed to origin.", runId, session.Branch);
        return session.Branch;
    }

    public async Task RemoveWorktreeAsync(string runId, bool force = false, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var args = force
            ? new List<string> { "worktree", "remove", "--force", session.Path }
            : new List<string> { "worktree", "remove", session.Path };

        var remove = await _git.RunAsync(session.RepositoryPath, args, GitTimeout, cancellationToken);
        if (remove.ExitCode != 0)
        {
            _logger.LogWarning("git worktree remove failed ({ExitCode}); pruning and deleting directory.", remove.ExitCode);
            await _git.RunAsync(session.RepositoryPath, ["worktree", "prune"], GitTimeout, cancellationToken);
            if (Directory.Exists(session.Path))
            {
                Directory.Delete(session.Path, recursive: true);
            }
        }

        await _sessions.SetStatusAsync(runId, WorktreeStatus.Removed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(string runId, CancellationToken cancellationToken = default)
    {
        _ = await RequireSessionAsync(runId, cancellationToken);
        await _sessions.SetStatusAsync(runId, WorktreeStatus.Completed, cancellationToken);
    }

    /// <summary>Marks a run's worktree failed; honours <c>RetainOnFailure</c> (RF-004).</summary>
    public async Task MarkFailedAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);
        await _sessions.SetStatusAsync(runId, WorktreeStatus.Failed, cancellationToken);
        if (session.RetainOnFailure)
        {
            await _sessions.SetStatusAsync(runId, WorktreeStatus.RetainedForInspection, cancellationToken);
        }
    }

    /// <summary>
    /// Returns <paramref name="baseBranch"/> when the ref resolves in the clone;
    /// otherwise falls back to the remote default (<c>origin/HEAD</c>) or
    /// <c>HEAD</c>. The resolved ref is what the session records — diffs and
    /// pushes must reference a base that actually exists.
    /// </summary>
    private async Task<string> ResolveBaseRefAsync(
        string repositoryPath,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var verify = await _git.RunAsync(
            repositoryPath,
            ["rev-parse", "--verify", "--quiet", baseBranch],
            GitTimeout,
            cancellationToken);
        if (verify.ExitCode == 0)
        {
            return baseBranch;
        }

        var originHead = await _git.RunAsync(
            repositoryPath,
            ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"],
            GitTimeout,
            cancellationToken);
        var resolved = originHead.ExitCode == 0 && !string.IsNullOrWhiteSpace(originHead.StandardOutput)
            ? originHead.StandardOutput.Trim()
            : "HEAD";
        _logger.LogInformation(
            "Base ref '{Base}' not found in {Repo}; worktree falls back to '{Resolved}'.",
            baseBranch, repositoryPath, resolved);
        return resolved;
    }

    private async Task<string> AddWorktreeWithUniqueBranchAsync(
        string repositoryPath,
        string path,
        string baseBranch,
        string runId,
        string taskSlug,
        CancellationToken cancellationToken)
    {
        var baseName = WorktreePaths.BranchName(runId, taskSlug);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var branch = attempt == 0 ? baseName : $"{baseName}-{attempt + 1}";
            var result = await _git.RunAsync(
                repositoryPath,
                ["worktree", "add", "-b", branch, path, baseBranch],
                GitTimeout,
                cancellationToken);

            if (result.ExitCode == 0)
            {
                return branch;
            }

            if (!result.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"git worktree add failed: {result.StandardError.Trim()}");
            }
        }

        throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Could not allocate a unique worktree branch.");
    }

    private async Task<WorktreeSessionDto> RequireSessionAsync(string runId, CancellationToken cancellationToken)
        => await _sessions.GetByRunIdAsync(runId, cancellationToken)
            ?? throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"No worktree session for run '{runId}'.");

    private static void EnsureSuccess(GitCommandResult result, string operation)
    {
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"{operation} failed{(result.TimedOut ? " (timeout)" : "")}: {result.StandardError.Trim()}");
        }
    }

    private static List<WorkspaceDiffFileDto> ParseStatus(string porcelain)
    {
        var files = new List<WorkspaceDiffFileDto>();
        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2].Trim() switch
            {
                "??" => "Untracked",
                "M" or "MM" => "Modified",
                "A" => "Added",
                "D" => "Deleted",
                "R" => "Renamed",
                var other => other,
            };

            files.Add(new WorkspaceDiffFileDto(line[3..].Trim(), status));
        }

        return files;
    }

    /// <summary>--numstat por arquivo (renames resolvem para o path novo).</summary>
    private static Dictionary<string, (int Insertions, int Deletions)> ParseNumstatPerFile(string numstat)
    {
        var map = new Dictionary<string, (int Insertions, int Deletions)>(StringComparer.Ordinal);
        foreach (var line in numstat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var insertions = int.TryParse(parts[0], out var i) ? i : 0;
            var deletions = int.TryParse(parts[1], out var d) ? d : 0;
            var path = parts[2];
            var arrow = path.IndexOf(" => ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                path = path[(arrow + 4)..].Replace("}", string.Empty, StringComparison.Ordinal);
            }

            map[path] = (insertions, deletions);
        }

        return map;
    }

    /// <summary>Confina <paramref name="relativePath"/> ao worktree — traversal absoluto/`..` → 400.</summary>
    private static string ResolveInsideWorktree(string worktreePath, string? relativePath)
    {
        var target = Path.GetFullPath(Path.Combine(worktreePath, relativePath ?? string.Empty));
        if (!WorktreePaths.IsUnder(worktreePath, target))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "Path escapes the worktree.");
        }

        return target;
    }

    private static string RelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsGitMetadata(FileSystemInfo info) =>
        string.Equals(info.Name, ".git", StringComparison.Ordinal);

    /// <summary>Symlinks apontando para fora do worktree são ocultados/negados (RF-003).</summary>
    private static bool EscapesViaLink(FileSystemInfo info, string root)
    {
        if (info.LinkTarget is null)
        {
            return false;
        }

        var real = info.ResolveLinkTarget(returnFinalTarget: true);
        return real is null || !WorktreePaths.IsUnder(root, real.FullName);
    }
}
