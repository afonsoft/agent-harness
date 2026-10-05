using System.Text;
using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-006/RNF-003: file-backed spill
/// store under <c>{dataDir}/spill/{runId}/</c>. Ids are restricted to
/// <c>[A-Za-z0-9_-]+</c> — no separators, no traversal (RNF-003).
/// </summary>
public sealed partial class ChatSpillStore(string dataDir) : ISpillStore
{
    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeId();

    private string SpillDir => Path.Join(dataDir, "spill");

    public string Save(string runId, int seq, string content)
    {
        var spillId = $"{runId}-{seq}";
        if (!SafeId().IsMatch(runId))
        {
            throw new ArgumentException($"invalid spill run id '{runId}'", nameof(runId));
        }

        var dir = Path.Join(SpillDir, runId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, spillId), content, Encoding.UTF8);
        return spillId;
    }

    public (string Content, int TotalChars)? Read(string spillId, int offset = 0, int limit = 16000)
    {
        var file = Resolve(spillId);
        if (file is null)
        {
            return null;
        }

        var content = File.ReadAllText(file, Encoding.UTF8);
        var total = content.Length;
        if (offset > 0)
        {
            content = offset < total ? content[offset..] : string.Empty;
        }

        if (limit > 0 && content.Length > limit)
        {
            content = content[..limit];
        }

        return (content, total);
    }

    public void DeleteRunDir(string runId)
    {
        if (!SafeId().IsMatch(runId))
        {
            return;
        }

        var dir = Path.Join(SpillDir, runId);
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup — a locked file must not fail retention.
        }
    }

    public int SweepOrphans(IReadOnlySet<string> aliveRunIds)
    {
        var removed = 0;
        if (!Directory.Exists(SpillDir))
        {
            return removed;
        }

        foreach (var dir in Directory.EnumerateDirectories(SpillDir))
        {
            var runId = Path.GetFileName(dir);
            if (aliveRunIds.Contains(runId))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (IOException)
            {
                // Leave locked dirs for the next sweep.
            }
        }

        return removed;
    }

    /// <summary>Full path for a spill id, or null when malformed/missing.</summary>
    private string? Resolve(string spillId)
    {
        if (string.IsNullOrWhiteSpace(spillId))
        {
            return null;
        }

        var dash = spillId.LastIndexOf('-');
        if (dash <= 0 || dash == spillId.Length - 1)
        {
            return null;
        }

        var runId = spillId[..dash];
        if (!SafeId().IsMatch(runId) || !SafeId().IsMatch(spillId))
        {
            return null;
        }

        var full = Path.Join(SpillDir, runId, spillId);
        return File.Exists(full) ? full : null;
    }
}
