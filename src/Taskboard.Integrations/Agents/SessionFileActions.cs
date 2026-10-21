using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-008 + §3: filesystem ops over
/// session artifacts — every path is confined to $HOME, opens go through the
/// platform handler, deletes are plain file removes.
/// </summary>
public sealed class SessionFileActions : ISessionFileActions
{
    private readonly string _home;
    private readonly ILogger<SessionFileActions>? _logger;

    public SessionFileActions(ILogger<SessionFileActions>? logger = null)
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), logger)
    {
    }

    internal SessionFileActions(string home, ILogger<SessionFileActions>? logger = null)
    {
        _home = home;
        _logger = logger;
    }

    public bool IsUnderHome(string path) => IsUnderHome(path, _home);

    internal static bool IsUnderHome(string path, string home)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(full, root, StringComparison.Ordinal);
    }

    public bool Exists(string path) =>
        IsUnderHome(path) && (File.Exists(path) || Directory.Exists(path));

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsUnderHome(path))
        {
            throw new InvalidOperationException($"Refusing to delete outside $HOME: {path}");
        }

        File.Delete(path);
        _logger?.LogInformation("session transcript deleted: {Path}", path);
        return Task.CompletedTask;
    }

    public async Task<bool> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!Exists(path))
        {
            return false;
        }

        var (cmd, arg) = OpenerFor(path);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = cmd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                ArgumentList = { arg },
            });
            if (proc is null)
            {
                return false;
            }

            // Openers fork-and-exit fast; wait briefly so we report spawn
            // failures but never hold the HTTP call on a misbehaving opener.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "platform open failed for {Path}", path);
            return false;
        }
    }

    private static (string Cmd, string Arg) OpenerFor(string path) =>
        OperatingSystem.IsWindows() ? ("explorer", path)
        : OperatingSystem.IsMacOS() ? ("open", path)
        : ("xdg-open", path);
}
