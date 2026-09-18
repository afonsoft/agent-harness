using System.Diagnostics;
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

    private readonly string _homeDirectory;
    private readonly int _port;
    private readonly string _publicPathPrefix;
    private readonly WorkspaceService _workspace;
    private readonly ILogger<CodeServerProcessManager> _logger;
    private readonly Func<string, string?> _locator;
    private readonly IStreamingProcessRunner _runner;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly object _gate = new();

    private Process? _process;
    private Task? _pumpTask;
    private bool _lastStartFailed;

    public CodeServerProcessManager(
        string homeDirectory,
        int port,
        WorkspaceService workspace,
        ILogger<CodeServerProcessManager> logger,
        Func<string, string?>? executableLocator = null,
        IStreamingProcessRunner? runner = null,
        Func<ProcessStartInfo, Process?>? processStarter = null,
        string publicPathPrefix = "/vscode")
    {
        _homeDirectory = homeDirectory;
        _port = port;
        _publicPathPrefix = publicPathPrefix;
        _workspace = workspace;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? StreamingProcessRunner.Instance;
        _processStarter = processStarter ?? Process.Start;
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
            IsRunning(),
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

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
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
            startInfo.ArgumentList.Add("Taskboard");
            // code-server is mounted at a subpath — without this its ports
            // panel and /proxy/<port> links point at the domain root and 404.
            startInfo.Environment["VSCODE_PROXY_URI"] = _publicPathPrefix + "/proxy/{{port}}";

            try
            {
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
