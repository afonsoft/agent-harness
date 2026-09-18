using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Vscode;
using Taskboard.Integrations.Agents;

namespace Taskboard.Integrations.Vscode;

/// <summary>
/// Executes the fixed, allowlisted code-server install command in the
/// background, capturing stdout/stderr line-by-line into a bounded buffer
/// (SPEC-20260917-vscode-web-workspace RF-004).
/// </summary>
public sealed class VscodeInstallService : IVscodeInstallService
{
    private const int LineCapacity = 500;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Allowlisted install command — code-server standalone (user-level, no sudo).</summary>
    internal static readonly string[] InstallArguments =
    [
        "-c",
        "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"
    ];

    internal const string InstallFileName = "bash";

    private static readonly Regex SecretPattern = new(
        @"(?i)(api[_-]?key|access[_-]?token|secret|password|authorization)\s*[=:]\s*\S+",
        RegexOptions.Compiled);

    // Bearer tokens must be redacted before the key:value pass — otherwise
    // "Authorization: Bearer tok" keeps the token ("Bearer" eaten as the value).
    private static readonly Regex BearerPattern = new(
        @"Bearer\s+\S+",
        RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly string _homeDirectory;
    private readonly ILogger<VscodeInstallService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly IStreamingProcessRunner _runner;

    private readonly Queue<AgentCliInstallLine> _lines = new(LineCapacity);
    private AgentCliInstallState _state = AgentCliInstallState.Idle;
    private DateTimeOffset? _startedAtUtc;
    private int? _exitCode;

    public VscodeInstallService(
        string homeDirectory,
        ILogger<VscodeInstallService> logger,
        Func<string, string?>? executableLocator = null,
        IStreamingProcessRunner? runner = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? StreamingProcessRunner.Instance;
    }

    public Task<VscodeInstallStatus> StartInstallAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state == AgentCliInstallState.Running)
            {
                return Task.FromResult(Snapshot());
            }

            _lines.Clear();
            _state = AgentCliInstallState.Running;
            _startedAtUtc = DateTimeOffset.UtcNow;
            _exitCode = null;

            if (_locator("curl") is null)
            {
                Append("stderr", "missing prerequisite: 'curl' not found on PATH");
                _state = AgentCliInstallState.Failed;
                return Task.FromResult(Snapshot());
            }
        }

        _ = Task.Run(ExecuteAsync, CancellationToken.None);
        return Task.FromResult(Snapshot());
    }

    public VscodeInstallStatus GetStatus()
    {
        lock (_gate)
        {
            return Snapshot();
        }
    }

    private async Task ExecuteAsync()
    {
        Append("info", $"$ {InstallFileName} {string.Join(' ', InstallArguments)}");
        try
        {
            using var timeout = new CancellationTokenSource(InstallTimeout);
            var exitCode = await _runner.RunAsync(
                InstallFileName,
                _homeDirectory,
                InstallArguments,
                (stream, line) => Append(stream, Sanitize(line)),
                timeout.Token).ConfigureAwait(false);

            if (exitCode == 0)
            {
                Append("info", "install finished successfully");
                Finish(AgentCliInstallState.Succeeded, exitCode);
            }
            else
            {
                Append("stderr", $"install failed with exit code {exitCode}");
                Finish(AgentCliInstallState.Failed, exitCode);
            }
        }
        catch (OperationCanceledException)
        {
            Append("stderr", "install timed out or was cancelled");
            Finish(AgentCliInstallState.Failed, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "code-server install run failed.");
            Append("stderr", Sanitize(ex.Message));
            Finish(AgentCliInstallState.Failed, null);
        }
    }

    private void Append(string stream, string content)
    {
        lock (_gate)
        {
            _lines.Enqueue(new AgentCliInstallLine(DateTimeOffset.UtcNow, stream, content));
            while (_lines.Count > LineCapacity)
            {
                _lines.Dequeue();
            }
        }
    }

    private void Finish(AgentCliInstallState state, int? exitCode)
    {
        lock (_gate)
        {
            _state = state;
            _exitCode = exitCode;
        }
    }

    private VscodeInstallStatus Snapshot() => new(_state, _startedAtUtc, _exitCode, _lines.ToArray());

    private static string Sanitize(string text) =>
        SecretPattern.Replace(
            BearerPattern.Replace(text, "Bearer <redacted>"),
            m => $"{m.Value.Split(['=', ':'], 2)[0]}=<redacted>");
}
