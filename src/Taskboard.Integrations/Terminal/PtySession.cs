using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Terminal;

/// <summary>
/// Interactive bash session inside a real PTY (via <c>script -qfc</c>) so
/// TUI/OAuth login flows of agent CLIs work. Output is pumped to
/// <see cref="OutputReceived"/>; input is written verbatim to the shell's
/// stdin (SPEC-20260917-cli-agents-terminal RF-005).
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    private readonly string _homeDirectory;
    private readonly ILogger _logger;
    private readonly Func<string, string?> _locator;
    private readonly int _cols;
    private readonly int _rows;

    private Process? _process;
    private Task? _pumpTask;
    private CancellationTokenSource? _pumpCts;
    private bool _disposed;

    public PtySession(string homeDirectory, ILogger logger, int cols = 120, int rows = 30,
        Func<string, string?>? executableLocator = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _cols = cols;
        _rows = rows;
        _locator = executableLocator ?? Agents.PathSearch.FindExecutable;
        LastActivityUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Raised for each chunk of terminal output (UTF-8 text, may contain ANSI escapes).</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised once when the shell process exits.</summary>
    public event Action<int>? Exited;

    /// <summary>Last time input was written — drives the idle timeout.</summary>
    public DateTimeOffset LastActivityUtc { get; private set; }

    /// <summary>Whether the underlying process is still running.</summary>
    public bool IsRunning
    {
        get
        {
            if (_disposed || _process is null)
            {
                return false;
            }

            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Spawns <c>script -qfc "stty …; exec bash -l" /dev/null</c>.</summary>
    public void Start()
    {
        if (_process is not null)
        {
            return;
        }

        var script = _locator("script")
            ?? throw new InvalidOperationException("'script' binary not found — terminal unavailable.");

        var startInfo = new ProcessStartInfo(script)
        {
            WorkingDirectory = _homeDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"stty cols {_cols} rows {_rows} 2>/dev/null; exec bash -l");
        startInfo.ArgumentList.Add("/dev/null");
        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["HOME"] = _homeDirectory;

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start terminal process.");
        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token));
    }

    /// <summary>Writes raw input to the shell (keys, paste, control chars).</summary>
    public async Task WriteAsync(string data)
    {
        if (_process is null)
        {
            return;
        }

        LastActivityUtc = DateTimeOffset.UtcNow;
        try
        {
            await _process.StandardInput.WriteAsync(data).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal input write failed.");
        }
    }

    /// <summary>Resizes the PTY via an injected <c>stty</c> command (echoes one line).</summary>
    public Task ResizeAsync(int cols, int rows)
    {
        if (cols is < 1 or > 500 || rows is < 1 or > 500)
        {
            return Task.CompletedTask;
        }

        return WriteAsync($"stty cols {cols} rows {rows}\n");
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _process!.StandardOutput
                    .ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                OutputReceived?.Invoke(new string(buffer, 0, read));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal output pump ended with error.");
        }
        finally
        {
            var exitCode = -1;
            try
            {
                exitCode = _process?.HasExited == true ? _process.ExitCode : -1;
            }
            catch
            {
            }

            Exited?.Invoke(exitCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await (_pumpCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch
        {
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Terminal process kill failed.");
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }

            _process.Dispose();
        }

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
