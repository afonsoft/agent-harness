using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Executes the allowlisted install command of an agent CLI in the background,
/// capturing stdout/stderr line-by-line into a bounded buffer
/// (SPEC-20260918-cli-agents-expansion RF-004/RF-005/RF-006).
/// </summary>
public sealed class AgentCliInstallService : IAgentCliInstallService
{
    private const int LineCapacity = 500;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private static readonly Regex SecretPattern = new(
        @"(?i)(api[_-]?key|access[_-]?token|secret|password|authorization)\s*[=:]\s*\S+|Bearer\s+\S+",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<AgentCliKind, InstallRun> _runs = new();
    private readonly string _homeDirectory;
    private readonly ILogger<AgentCliInstallService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly IStreamingProcessRunner _runner;

    public AgentCliInstallService(
        string homeDirectory,
        ILogger<AgentCliInstallService> logger,
        Func<string, string?>? executableLocator = null,
        IStreamingProcessRunner? runner = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? StreamingProcessRunner.Instance;
    }

    public Task<AgentCliInstallStatus> StartInstallAsync(AgentCliKind kind, CancellationToken cancellationToken = default)
    {
        var spec = AgentCliMap.GetSpec(kind)
            ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown CLI kind.");
        var run = _runs.GetOrAdd(kind, _ => new InstallRun());

        lock (run.Gate)
        {
            if (run.State == AgentCliInstallState.Running)
            {
                return Task.FromResult(run.Snapshot(kind));
            }

            run.Reset(kind);
            if (_locator(spec.Install.RequiredTool) is null)
            {
                run.Append("stderr", $"missing prerequisite: '{spec.Install.RequiredTool}' not found on PATH");
                run.Finish(AgentCliInstallState.Failed, null);
                return Task.FromResult(run.Snapshot(kind));
            }
        }

        _ = Task.Run(() => ExecuteAsync(run, kind, spec), CancellationToken.None);
        return Task.FromResult(run.Snapshot(kind));
    }

    public AgentCliInstallStatus GetStatus(AgentCliKind kind) =>
        _runs.TryGetValue(kind, out var run)
            ? run.Snapshot(kind)
            : new AgentCliInstallStatus(kind, AgentCliInstallState.Idle, null, null, []);

    private async Task ExecuteAsync(InstallRun run, AgentCliKind kind, AgentCliSpec spec)
    {
        run.Append("info", $"$ {spec.Install.FileName} {string.Join(' ', spec.Install.Arguments)}");
        try
        {
            using var timeout = new CancellationTokenSource(InstallTimeout);
            var exitCode = await _runner.RunAsync(
                spec.Install.FileName,
                _homeDirectory,
                spec.Install.Arguments,
                (stream, line) => run.Append(stream, Sanitize(line)),
                timeout.Token).ConfigureAwait(false);

            if (exitCode == 0)
            {
                run.Append("info", "install finished successfully");
                run.Finish(AgentCliInstallState.Succeeded, exitCode);
            }
            else
            {
                run.Append("stderr", $"install failed with exit code {exitCode}");
                run.Finish(AgentCliInstallState.Failed, exitCode);
            }
        }
        catch (OperationCanceledException)
        {
            run.Append("stderr", "install timed out or was cancelled");
            run.Finish(AgentCliInstallState.Failed, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Install run failed for {Kind}.", kind);
            run.Append("stderr", Sanitize(ex.Message));
            run.Finish(AgentCliInstallState.Failed, null);
        }
    }

    private static string Sanitize(string text) =>
        SecretPattern.Replace(text, m =>
            m.Value.Contains('=', StringComparison.Ordinal) || m.Value.Contains(':', StringComparison.Ordinal)
                ? $"{m.Value.Split(['=', ':'], 2)[0]}=<redacted>"
                : "Bearer <redacted>");

    private sealed class InstallRun
    {
        public object Gate { get; } = new();

        private readonly Queue<AgentCliInstallLine> _lines = new(LineCapacity);
        private AgentCliInstallState _state = AgentCliInstallState.Idle;
        private DateTimeOffset? _startedAtUtc;
        private int? _exitCode;

        public AgentCliInstallState State => _state;

        public void Reset(AgentCliKind kind)
        {
            _lines.Clear();
            _state = AgentCliInstallState.Running;
            _startedAtUtc = DateTimeOffset.UtcNow;
            _exitCode = null;
        }

        public void Append(string stream, string content)
        {
            lock (Gate)
            {
                _lines.Enqueue(new AgentCliInstallLine(DateTimeOffset.UtcNow, stream, content));
                while (_lines.Count > LineCapacity)
                {
                    _lines.Dequeue();
                }
            }
        }

        public void Finish(AgentCliInstallState state, int? exitCode)
        {
            lock (Gate)
            {
                _state = state;
                _exitCode = exitCode;
            }
        }

        public AgentCliInstallStatus Snapshot(AgentCliKind kind)
        {
            lock (Gate)
            {
                return new AgentCliInstallStatus(kind, _state, _startedAtUtc, _exitCode, _lines.ToArray());
            }
        }
    }
}
