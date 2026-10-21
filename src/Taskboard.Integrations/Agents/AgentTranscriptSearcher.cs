using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-002 + NFR: transcript search via
/// <c>rg</c> when on PATH (timeout 5s, ≤100 matches), otherwise an in-process
/// streaming line scan with the same contract — case-insensitive literal
/// match, snippet = first matching line trimmed to ≤160 chars, paths confined
/// to <c>$HOME</c>.
/// </summary>
public sealed class AgentTranscriptSearcher : IAgentTranscriptSearcher
{
    internal const int MaxSnippetChars = 160;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(5);

    private readonly string _home;
    private readonly ILogger<AgentTranscriptSearcher>? _logger;
    private readonly string? _rgPath;
    private readonly Func<string, string, CancellationToken, Task<string?>>? _rgOverride;

    public AgentTranscriptSearcher(ILogger<AgentTranscriptSearcher>? logger = null)
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
              logger, FindRipgrep())
    {
    }

    internal AgentTranscriptSearcher(
        string home,
        ILogger<AgentTranscriptSearcher>? logger = null,
        string? rgPath = null,
        Func<string, string, CancellationToken, Task<string?>>? rgOverride = null)
    {
        _home = home;
        _logger = logger;
        _rgPath = rgPath;
        _rgOverride = rgOverride;
    }

    public async Task<IReadOnlyDictionary<string, string>> SearchAsync(
        IReadOnlyList<string> paths, string needle, int maxMatches,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(needle) || maxMatches <= 0)
        {
            return results;
        }

        var safe = paths
            .Where(p => IsUnderHome(p) && File.Exists(p))
            .Take(500)
            .ToList();
        if (safe.Count == 0)
        {
            return results;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SearchTimeout);
        try
        {
            if (_rgPath is not null || _rgOverride is not null)
            {
                return await SearchRipgrepAsync(safe, needle, maxMatches, timeout.Token)
                    .ConfigureAwait(false);
            }

            return SearchInProcess(safe, needle, maxMatches, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger?.LogDebug("transcript search timed out after {Timeout}s", SearchTimeout.TotalSeconds);
            return results;
        }
    }

    private bool IsUnderHome(string path)
    {
        var full = Path.GetFullPath(path);
        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_home));
        return full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(full, home, StringComparison.Ordinal);
    }

    // ---- rg path ----

    private async Task<IReadOnlyDictionary<string, string>> SearchRipgrepAsync(
        List<string> paths, string needle, int maxMatches, CancellationToken ct)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var args = new List<string> { "-l", "--max-count", "1", "-i", "-F", "-e", needle, "--" };
        args.AddRange(paths);
        var matched = await RunRgAsync(args, ct).ConfigureAwait(false);
        foreach (var path in matched.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (results.Count >= maxMatches)
            {
                break;
            }

            var hit = await RunRgAsync(
                ["-m", "1", "-n", "-i", "-F", "-e", needle, "--", path.Trim()], ct)
                .ConfigureAwait(false);
            var line = hit?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (line is not null)
            {
                var snippet = line[(line.IndexOf(':') + 1)..].Trim();
                results[path.Trim()] = snippet.Length > MaxSnippetChars
                    ? snippet[..MaxSnippetChars]
                    : snippet;
            }
        }

        return results;
    }

    private async Task<string> RunRgAsync(List<string> args, CancellationToken ct)
    {
        if (_rgOverride is not null)
        {
            return await _rgOverride(string.Join(' ', args), args[^1], ct)
                .ConfigureAwait(false) ?? string.Empty;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _rgPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start rg");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return stdout;
    }

    // ---- in-process fallback ----

    private static IReadOnlyDictionary<string, string> SearchInProcess(
        List<string> paths, string needle, int maxMatches, CancellationToken ct)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var buffer = new char[8192];
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (results.Count >= maxMatches)
            {
                break;
            }

            var snippet = FirstMatchingLine(path, needle, buffer, ct);
            if (snippet is not null)
            {
                results[path] = snippet;
            }
        }

        return results;
    }

    private static string? FirstMatchingLine(
        string path, string needle, char[] buffer, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 8192);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    var trimmed = line.Trim();
                    return trimmed.Length > MaxSnippetChars
                        ? trimmed[..MaxSnippetChars]
                        : trimmed;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? FindRipgrep()
    {
        var name = OperatingSystem.IsWindows() ? "rg.exe" : "rg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Join(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
