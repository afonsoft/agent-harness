using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Vscode;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;

namespace Taskboard.Integrations.Vscode;

/// <summary>
/// Manages the code-server child process (SPEC-20260917-vscode-web-workspace
/// RF-005): lazy spawn bound to loopback only with <c>--auth none</c> — the
/// Taskboard-authenticated YARP route is the only door in.
/// </summary>
public sealed class CodeServerProcessManager : ICodeServerManager, IAsyncDisposable
{
    /// <summary>Default loopback port for code-server.</summary>
    public const int DefaultPort = 8377;

    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _homeDirectory;
    private readonly int _port;
    private readonly string _publicPathPrefix;
    private readonly WorkspaceService _workspace;
    private readonly ILogger<CodeServerProcessManager> _logger;
    private readonly Func<string, string?> _locator;
    private readonly IStreamingProcessRunner _runner;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly Func<int, CancellationToken, Task<bool>> _portProbe;
    private readonly TimeSpan _readyTimeout;
    private readonly object _gate = new();

    private Process? _process;
    private Task? _pumpTask;
    private Task<VscodeStatus>? _restartInFlight;
    private bool _lastStartFailed;
    private bool _listening;

    public CodeServerProcessManager(
        string homeDirectory,
        int port,
        WorkspaceService workspace,
        ILogger<CodeServerProcessManager> logger,
        Func<string, string?>? executableLocator = null,
        IStreamingProcessRunner? runner = null,
        Func<ProcessStartInfo, Process?>? processStarter = null,
        string publicPathPrefix = "/vscode",
        Func<int, CancellationToken, Task<bool>>? portProbe = null,
        TimeSpan? readyTimeout = null)
    {
        _homeDirectory = homeDirectory;
        _port = port;
        _publicPathPrefix = publicPathPrefix;
        _workspace = workspace;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? StreamingProcessRunner.Instance;
        _processStarter = processStarter ?? Process.Start;
        _portProbe = portProbe ?? TcpProbeAsync;
        _readyTimeout = readyTimeout ?? DefaultReadyTimeout;
    }

    /// <summary>Loopback base URL the reverse proxy targets.</summary>
    public string BaseUrl => $"http://127.0.0.1:{_port}";

    public async Task<VscodeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var binary = FindBinary();
        string? version = null;
        if (binary is not null)
        {
            version = await ProbeVersionAsync(binary, cancellationToken).ConfigureAwait(false);
        }

        return new VscodeStatus(
            binary is not null,
            binary,
            version,
            IsRunning() && _listening,
            _port,
            _homeDirectory,
            _workspace.Root);
    }

    public async Task<VscodeStatus> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning())
        {
            var binary = FindBinary();
            if (binary is not null)
            {
                Start(binary);
            }
        }

        if (IsRunning())
        {
            // The process is alive but may still be binding its listener —
            // proxying now would surface as a raw 502 (connection refused).
            await WaitForListeningAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20260920 RF-008: kill → spawn → wait-listening as a single-flight
    /// operation — concurrent callers share the same in-flight task instead of
    /// each running their own kill/spawn (which would kill the process the
    /// first caller just created).
    /// </summary>
    public Task<VscodeStatus> RestartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return _restartInFlight ??= RestartCoreAsync(cancellationToken);
        }
    }

    private async Task<VscodeStatus> RestartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var binary = FindBinary();
            lock (_gate)
            {
                StopLocked();
                if (binary is not null)
                {
                    StartLocked(binary);
                }
            }

            if (IsRunning())
            {
                await WaitForListeningAsync(cancellationToken).ConfigureAwait(false);
            }

            return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _restartInFlight = null;
            }
        }
    }

    /// <summary>Kills the current child (if any) and waits briefly for it to exit — caller must hold <see cref="_gate"/>.</summary>
    private void StopLocked()
    {
        _listening = false;
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "code-server kill on restart failed.");
        }
        finally
        {
            process.Dispose();
        }
    }

    internal string? FindBinary()
    {
        var onPath = _locator("code-server");
        if (onPath is not null)
        {
            return onPath;
        }

        // Standalone install lands in ~/.local/bin.
        var standalone = Path.Join(_homeDirectory, ".local", "bin", "code-server");
        return File.Exists(standalone) ? standalone : null;
    }

    private bool IsRunning()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void Start(string binary)
    {
        lock (_gate)
        {
            StartLocked(binary);
        }
    }

    /// <summary>Spawn path — caller must hold <see cref="_gate"/>.</summary>
    private void StartLocked(string binary)
    {
        if (IsRunning())
        {
            return;
        }

        var startInfo = new ProcessStartInfo(binary)
        {
            WorkingDirectory = _workspace.EnsureRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--bind-addr");
        startInfo.ArgumentList.Add($"127.0.0.1:{_port}");
        startInfo.ArgumentList.Add("--auth");
        startInfo.ArgumentList.Add("none");
        startInfo.ArgumentList.Add("--disable-telemetry");
        startInfo.ArgumentList.Add("--disable-update-check");
        // Agent runs keep cloning fresh repos under the workspace root;
        // without this every new folder opens in Restricted Mode.
        startInfo.ArgumentList.Add("--disable-workspace-trust");
        startInfo.ArgumentList.Add("--app-name");
        startInfo.ArgumentList.Add("Harness");
        // code-server is mounted at a subpath — without this its ports
        // panel and /proxy/<port> links point at the domain root and 404.
        startInfo.Environment["VSCODE_PROXY_URI"] = _publicPathPrefix + "/proxy/{{port}}";

        try
        {
            _listening = false;
            _process = _processStarter(startInfo);
            _lastStartFailed = _process is null;
        }
        catch (Exception ex)
        {
            _lastStartFailed = true;
            _logger.LogWarning(ex, "code-server failed to start.");
            return;
        }

        if (_process is not null)
        {
            _pumpTask = Task.Run(PumpAsync);
            _logger.LogInformation("code-server started (pid {Pid}, {Url}).", _process.Id, BaseUrl);
        }
    }

    /// <summary>
    /// Polls the loopback port until code-server accepts a connection or the
    /// ready timeout elapses. Once bound, the listener stays up for the life
    /// of the process, so a single successful probe marks it servable.
    /// </summary>
    private async Task WaitForListeningAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readyTimeout);
        try
        {
            while (true)
            {
                if (!IsRunning())
                {
                    return; // died during startup — status will report not-running
                }

                if (await _portProbe(_port, timeout.Token).ConfigureAwait(false))
                {
                    _listening = true;
                    _logger.LogInformation("code-server is listening on {Url}.", BaseUrl);
                    return;
                }

                await Task.Delay(ReadyPollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "code-server did not listen on {Url} within {Seconds}s.",
                    BaseUrl, _readyTimeout.TotalSeconds);
            }
        }
    }

    private static async Task<bool> TcpProbeAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            // Output is drained so the child never blocks on a full pipe;
            // content is not logged (may embed editor internals).
            var stdout = _process!.StandardOutput.ReadToEndAsync();
            var stderr = _process.StandardError.ReadToEndAsync();
            await _process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr);
            _logger.LogInformation("code-server exited with code {ExitCode}.", _process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "code-server pump ended.");
        }
    }

    private async Task<string?> ProbeVersionAsync(string binary, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VersionProbeTimeout);
            var line = (string?)null;
            await _runner.RunAsync(
                binary,
                _homeDirectory,
                ["--version"],
                (_, l) => { line ??= l; },
                timeout.Token).ConfigureAwait(false);
            return line;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "code-server --version probe failed.");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "code-server kill on shutdown failed.");
        }
        finally
        {
            process.Dispose();
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
}
