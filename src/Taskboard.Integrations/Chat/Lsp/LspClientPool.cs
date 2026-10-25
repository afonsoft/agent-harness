using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Lsp;

/// <summary>
/// SPEC-20261025 C4: one LSP server process per workspace, idle-reaped.
/// Server comes from <c>Taskboard:Chat:Lsp:Server</c> (default
/// <c>csharp-ls</c>, stdio) + optional <c>Taskboard:Chat:Lsp:Args</c>;
/// <c>Taskboard:Chat:Lsp:IdleMinutes</c> bounds an unused session
/// (default 5). A failed spawn/handshake drops the entry — the next call
/// retries fresh.
/// </summary>
public sealed class LspClientPool(IConfiguration configuration) : ILspClientPool, IDisposable
{
    public const string ServerKey = "Taskboard:Chat:Lsp:Server";
    public const string ArgsKey = "Taskboard:Chat:Lsp:Args";
    public const string IdleMinutesKey = "Taskboard:Chat:Lsp:IdleMinutes";
    public const string DefaultServer = "csharp-ls";

    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record Entry(LspClient Client, Process Process, DateTimeOffset LastUsed);

    private string Server => configuration[ServerKey] is { Length: > 0 } s ? s : DefaultServer;

    /// <inheritdoc/>
    public bool IsServerAvailable()
    {
        var server = Server;
        if (server.Contains('/') || server.Contains('\\'))
        {
            return File.Exists(server); // explicit path
        }
        return ResolveOnPath(server) is not null;
    }

    /// <inheritdoc/>
    public async Task<ILspClient?> GetAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var server = Server;
        var root = Path.GetFullPath(workspacePath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await GetCoreAsync(server, root, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ILspClient?> GetCoreAsync(
        string server, string root, CancellationToken cancellationToken)
    {
        var idle = TimeSpan.FromMinutes(
            int.TryParse(configuration[IdleMinutesKey], out var m) && m > 0 ? m : 5);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _sessions)
        {
            if (now - entry.LastUsed > idle || entry.Process.HasExited)
            {
                if (_sessions.TryRemove(key, out var stale))
                {
                    DisposeEntry(stale);
                }
            }
        }

        if (_sessions.TryGetValue(root, out var live))
        {
            _sessions[root] = live with { LastUsed = now };
            return live.Client;
        }

        var resolved = server.Contains('/') || server.Contains('\\')
            ? server
            : ResolveOnPath(server);
        if (resolved is null)
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolved,
                WorkingDirectory = root,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in (configuration[ArgsKey] ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"lsp: failed to start {server}");
            // stderr drained to nowhere — server log noise must not fill the
            // pipe buffer (a full buffer blocks the writer = dead server).
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var client = new LspClient(
                process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
            try
            {
                await client.InitializeAsync(new Uri(root + "/", UriKind.Absolute).AbsoluteUri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                try { process.Kill(entireProcessTree: true); } catch { /* gone */ }
                process.Dispose();
                return null;
            }
            _sessions[root] = new Entry(client, process, now);
            return client;
        }
        catch
        {
            return null;
        }
    }

    private static void DisposeEntry(Entry entry)
    {
        try { entry.Client.Dispose(); } catch { /* best effort */ }
        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill(entireProcessTree: true);
            }
        }
        catch { /* already gone */ }
        try { entry.Process.Dispose(); } catch { /* best effort */ }
    }

    private static string? ResolveOnPath(string binary)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return pathEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Join(dir.Trim(), binary))
            .FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        foreach (var (_, entry) in _sessions)
        {
            DisposeEntry(entry);
        }
        _sessions.Clear();
    }
}
