using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat;

/// <summary>
/// Git-aware workspace diff for the deliverables card
/// (SPEC-20261005-chat-attachments-feedback RF-007). Snapshot = porcelain +
/// numstat vs HEAD at run start; diff = the numstat delta plus untracked
/// files that appeared since. Outside a work tree the snapshot is null and
/// the executor falls back to the file-edit tracker.
/// </summary>
public sealed class GitWorkspaceDiffService(IGitCommandRunner git) : IChatWorkspaceDiffService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public async Task<ChatWorkspaceSnapshot?> SnapshotAsync(
        string workspacePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return null;
        }

        var inside = await git.RunAsync(
            workspacePath, ["rev-parse", "--is-inside-work-tree"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (inside.ExitCode != 0 || !string.Equals(inside.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            return null;
        }

        var porcelain = await git.RunAsync(
            workspacePath, ["status", "--porcelain=v1", "-z"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        var numstat = await git.RunAsync(
            workspacePath, ["diff", "--numstat", "HEAD"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        return new ChatWorkspaceSnapshot(
            porcelain.ExitCode == 0 ? porcelain.StandardOutput : string.Empty,
            numstat.ExitCode == 0 ? numstat.StandardOutput : string.Empty);
    }

    public async Task<IReadOnlyList<ChatDeliverableDto>> DiffAsync(
        string workspacePath, ChatWorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var deliverables = new List<ChatDeliverableDto>();

        var numstatNow = await git.RunAsync(
            workspacePath, ["diff", "--numstat", "HEAD"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        var before = ParseNumstat(snapshot.Numstat);
        var after = ParseNumstat(numstatNow.ExitCode == 0 ? numstatNow.StandardOutput : string.Empty);
        foreach (var (path, counts) in after)
        {
            // Delta vs the baseline — a file already dirty at run start only
            // reports the lines the run added on top.
            var added = counts.Added - (before.TryGetValue(path, out var b) ? b.Added : 0);
            var removed = counts.Removed - (before.TryGetValue(path, out var b2) ? b2.Removed : 0);
            if (added == 0 && removed == 0)
            {
                continue;
            }

            deliverables.Add(new ChatDeliverableDto(
                path,
                added == 0 && removed == 0 ? null : Math.Max(0, added),
                added == 0 && removed == 0 ? null : Math.Max(0, removed),
                ChatDeliverableSources.Git));
        }

        // New untracked files (?? entries that were not in the baseline).
        var porcelainNow = await git.RunAsync(
            workspacePath, ["status", "--porcelain=v1", "-z"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        var untrackedBefore = ParseUntracked(snapshot.Porcelain);
        foreach (var path in ParseUntracked(porcelainNow.ExitCode == 0 ? porcelainNow.StandardOutput : string.Empty))
        {
            if (untrackedBefore.Contains(path) || after.ContainsKey(path))
            {
                continue;
            }

            int? added = null;
            var full = Path.Join(workspacePath, path);
            try
            {
                if (File.Exists(full) && new FileInfo(full).Length < 512 * 1024)
                {
                    added = (await File.ReadAllLinesAsync(full, cancellationToken).ConfigureAwait(false)).Length;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            deliverables.Add(new ChatDeliverableDto(path, added, null, ChatDeliverableSources.Git));
        }

        return deliverables;
    }

    private static Dictionary<string, (int Added, int Removed)> ParseNumstat(string numstat)
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var line in numstat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            // "-\t-\t" marks binary files — counts stay unknown.
            if (parts.Length >= 3
                && int.TryParse(parts[0], out var added)
                && int.TryParse(parts[1], out var removed))
            {
                map[parts[2]] = (added, removed);
            }
        }

        return map;
    }

    private static HashSet<string> ParseUntracked(string porcelain)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.StartsWith("??", StringComparison.Ordinal) && entry.Length > 3)
            {
                set.Add(entry[3..].Trim());
            }
        }

        return set;
    }
}
