using Taskboard;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// Parsing/contagem compartilhada entre os produtores de
/// <see cref="WorkspaceDiffDto"/> — <see cref="GitWorktreeManager"/> (worktree
/// vs base branch) e <see cref="Taskboard.Integrations.Chat.GitWorkspaceDiffService"/>
/// (workspace vs HEAD, SPEC-20261011-chat-workspace-panel). Extraído para
/// evitar duplicar a lógica frágil de porcelain/numstat/nome-status e do
/// sniff de binário.
/// </summary>
internal static class GitDiffParser
{
    /// <summary>Janela de sniff para detectar binário (NUL nos primeiros 8 KB).</summary>
    internal const int BinarySniffBytes = 8 * 1024;

    /// <summary>--name-status vs base: tracked modificados/adicionados/deletados/renomeados
    /// (commitados ou não). Renames resolvem para o path novo.</summary>
    internal static List<WorkspaceDiffFileDto> ParseNameStatus(string output)
    {
        var files = new List<WorkspaceDiffFileDto>();
        foreach (var parts in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')))
        {
            if (parts.Length < 2 || parts[0].Length == 0)
            {
                continue;
            }

            var status = parts[0][0] switch
            {
                'M' => "Modified",
                'A' => "Added",
                'D' => "Deleted",
                'R' or 'C' => "Renamed",
                'T' => "TypeChanged",
                var other => other.ToString(),
            };

            files.Add(new WorkspaceDiffFileDto(parts[^1].Trim(), status));
        }

        return files;
    }

    internal static List<WorkspaceDiffFileDto> ParseStatus(string porcelain)
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

            var path = line[3..].Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                path = path[(arrow + 4)..];
            }

            files.Add(new WorkspaceDiffFileDto(path, status));
        }

        return files;
    }

    /// <summary>--numstat por arquivo (renames resolvem para o path novo).</summary>
    internal static Dictionary<string, (int Insertions, int Deletions)> ParseNumstatPerFile(string numstat)
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

    /// <summary>Insertions de uma entrada Untracked: direto do mapa ou soma sob um 'dir/' colapsado.</summary>
    internal static int UntrackedInsertions(WorkspaceDiffFileDto file, IReadOnlyDictionary<string, int> untrackedLines)
    {
        if (file.Status != "Untracked")
        {
            return 0;
        }

        // `status --porcelain` colapsa diretórios só-untracked como 'dir/'.
        if (file.Path.EndsWith('/'))
        {
            var sum = 0;
            foreach (var (path, count) in untrackedLines)
            {
                if (path.StartsWith(file.Path, StringComparison.Ordinal))
                {
                    sum += count;
                }
            }

            return sum;
        }

        return untrackedLines.TryGetValue(file.Path, out var lines) ? lines : 0;
    }

    /// <summary>Insertions de um arquivo untracked (linhas lidas do disco, por path).</summary>
    internal static async Task<Dictionary<string, int>> CountUntrackedLinesAsync(
        IGitCommandRunner git, string worktreePath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // --exclude-standard honra .gitignore — mesmo conjunto que o '??' do
        // porcelain cobre (incluindo o interior de diretórios colapsados).
        var ls = await git.RunAsync(
            worktreePath, ["ls-files", "--others", "--exclude-standard"], timeout, cancellationToken);
        if (ls.TimedOut || ls.ExitCode != 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"git ls-files --others failed{(ls.TimedOut ? " (timeout)" : "")}: {ls.StandardError.Trim()}");
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in ls.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var rel = line.TrimEnd();
            if (rel.Length > 0)
            {
                map[rel] = CountTextLines(worktreePath, rel);
            }
        }

        return map;
    }

    /// <summary>
    /// Linhas de texto de um arquivo (sniff de binário idêntico ao do
    /// explorer); arquivo ausente, diretório ou binário → 0, como o
    /// <c>-</c> que o numstat mostraria.
    /// </summary>
    internal static int CountTextLines(string worktreePath, string relativePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Join(worktreePath, relativePath));
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length == 0 || EscapesViaLink(info, worktreePath))
            {
                return 0;
            }

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return CountLinesInStream(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0; // corrida de deleção/lock — não pode derrubar o diff
        }
    }

    /// <summary>Conta '\n' no stream; sniff dos primeiros bytes aborta em binário
    /// (retorna 0, como o <c>-</c> do numstat).</summary>
    internal static int CountLinesInStream(FileStream stream)
    {
        var buffer = new byte[64 * 1024];
        var lines = 0;
        var sniffed = 0;
        var lastByte = -1;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (SniffIsBinary(buffer, read, ref sniffed))
            {
                return 0;
            }

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    lines++;
                }
            }

            lastByte = buffer[read - 1];
        }

        // Última linha sem '\n' final também conta, como no diff do git.
        return lastByte >= 0 && lastByte != '\n' ? lines + 1 : lines;
    }

    private static bool SniffIsBinary(byte[] buffer, int read, ref int sniffed)
    {
        if (sniffed >= BinarySniffBytes)
        {
            return false;
        }

        var window = Math.Min(read, BinarySniffBytes - sniffed);
        sniffed += window;
        return buffer.AsSpan(0, window).IndexOf((byte)0) >= 0;
    }

    /// <summary>Symlinks apontando para fora do worktree são ocultados/negados (RF-003).</summary>
    internal static bool EscapesViaLink(FileSystemInfo info, string root)
    {
        if (info.LinkTarget is null)
        {
            return false;
        }

        var real = info.ResolveLinkTarget(returnFinalTarget: true);
        return real is null || !WorktreePaths.IsUnder(root, real.FullName);
    }
}
