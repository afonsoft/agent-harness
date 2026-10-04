using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Delegation;

/// <summary>
/// SPEC-20261005 RF-004: hosted dispatcher for the delegation DAG — promotes
/// pending tasks whose deps are done, sweeps stale heartbeats, guards stale
/// bases, and runs ready tasks up to <c>Taskboard:Delegation:MaxConcurrent</c>.
/// Builtin CLIs go through the orchestration queue; custom defs run inline via
/// the shared <see cref="CustomCliRunner"/> template/stdin path.
/// </summary>
public sealed class DelegationDispatcherService : BackgroundService
{
    internal const string ConfigSection = "Taskboard:Delegation";
    private const int ResultSummaryMaxLength = 4000;
    private static readonly TimeSpan GitProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RunPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CustomCliTimeout = TimeSpan.FromSeconds(120);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentOrchestrationService _orchestration;
    private readonly IGitCommandRunner _git;
    private readonly ISecretRedactor _redactor;
    private readonly ILogger<DelegationDispatcherService> _logger;
    private readonly TimeProvider _time;
    private readonly int _maxConcurrent;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly ConcurrentDictionary<string, Task> _inflight = new(StringComparer.Ordinal);

    public DelegationDispatcherService(
        IServiceScopeFactory scopeFactory,
        IAgentOrchestrationService orchestration,
        IGitCommandRunner git,
        ISecretRedactor redactor,
        IConfiguration configuration,
        ILogger<DelegationDispatcherService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _orchestration = orchestration;
        _git = git;
        _redactor = redactor;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _maxConcurrent = Math.Max(1, configuration.GetValue($"{ConfigSection}:MaxConcurrent", 4));
        _pollInterval = TimeSpan.FromSeconds(
            Math.Max(1, configuration.GetValue($"{ConfigSection}:PollIntervalSeconds", 3)));
        _heartbeatTimeout = TimeSpan.FromSeconds(
            Math.Max(30, configuration.GetValue($"{ConfigSection}:HeartbeatTimeoutSeconds", 300)));
    }

    /// <summary>Inflight count exposed for tests and the inspection endpoint.</summary>
    public int InflightCount => _inflight.Values.Count(t => !t.IsCompleted);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "delegation dispatcher tick failed");
            }

            await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One dispatcher pass — internal for deterministic unit tests.</summary>
    internal async Task TickAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        IReadOnlyList<DelegationTaskDto> open;

        using (var scope = _scopeFactory.CreateScope())
        {
            var delegation = scope.ServiceProvider.GetRequiredService<IDelegationService>();
            await delegation.ResolveDependenciesAsync(ct).ConfigureAwait(false);
            await delegation.SweepStaleHeartbeatsAsync(_heartbeatTimeout, now, ct).ConfigureAwait(false);
            open = await delegation.ListOpenAsync(ct).ConfigureAwait(false);
        }

        // RF-005: stale-base guard — ready task whose repo HEAD moved → stale.
        var staled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in open.Where(t =>
            t.Status == DelegationTaskStatus.Ready
            && t.BaseCommitSha is not null
            && t.RepositoryPath is not null))
        {
            var head = await TryGetHeadShaAsync(task.RepositoryPath!, ct).ConfigureAwait(false);
            if (head is null || string.Equals(head, task.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reason = $"base moved: {Short(task.BaseCommitSha!)} → {Short(head)}";
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDelegationService>()
                .MarkStaleAsync(task.Id, reason, ct).ConfigureAwait(false);
            staled.Add(task.Id);
        }

        var capacity = _maxConcurrent - InflightCount;
        if (capacity <= 0)
        {
            return;
        }

        foreach (var taskId in open
            .Where(t => t.Status == DelegationTaskStatus.Ready && !staled.Contains(t.Id))
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id))
        {
            if (capacity <= 0)
            {
                break;
            }

            using var scope = _scopeFactory.CreateScope();
            var delegation = scope.ServiceProvider.GetRequiredService<IDelegationService>();
            if (await delegation.BeginRunAsync(taskId, now, ct).ConfigureAwait(false) is null)
            {
                continue;
            }

            _inflight[taskId] = Task.Run(
                () => ExecuteTaskAsync(taskId, ct), CancellationToken.None);
            capacity--;
        }
    }

    private static string Short(string sha) =>
        sha[..Math.Min(12, sha.Length)];

    /// <summary>Executes one task: builtin → orchestration queue; custom def → inline runner.</summary>
    private async Task ExecuteTaskAsync(string taskId, CancellationToken ct)
    {
        try
        {
            var task = await GetTaskAsync(taskId, ct).ConfigureAwait(false);
            if (task is null || task.Status != DelegationTaskStatus.Running)
            {
                return;
            }

            if (Enum.TryParse(task.CliName, ignoreCase: true, out AgentType agentType)
                && AgentCliMap.CliKindFor(agentType) is { } cliKind
                && AgentCliMap.GetSpec(cliKind) is not null)
            {
                await ExecuteBuiltinAsync(task, agentType, ct).ConfigureAwait(false);
            }
            else
            {
                await ExecuteCustomDefAsync(task, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutting down — the heartbeat sweep will reclaim the task.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "delegated task {TaskId} crashed", taskId);
            await FinishAsync(taskId, ok: false, $"execution crashed: {ex.Message}")
                .ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(taskId, out _);
        }
    }

    private async Task<DelegationTaskDto?> GetTaskAsync(string taskId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDelegationTaskRepository>();
        return await repo.GetAsync(taskId, ct).ConfigureAwait(false);
    }

    private async Task ExecuteBuiltinAsync(DelegationTaskDto task, AgentType agentType, CancellationToken ct)
    {
        var issueId = $"task:{task.Id}";
        var repoPath = await ResolveWorkdirAsync(task, ct).ConfigureAwait(false);
        var request = new AgentExecutionRequest(
            IssueId: issueId,
            IssueNumber: 0,
            RepositoryFullName: string.Empty,
            RepoPath: repoPath,
            Branch: null,
            Scope: null,
            Instructions: $"(delegated task {task.Id})\n\n{task.Prompt}",
            AgentType: agentType,
            OmitModelFlag: true);

        if (!await _orchestration.EnqueueAsync(request, ct).ConfigureAwait(false))
        {
            await FinishAsync(task.Id, ok: false, "agent not eligible (disabled or unauthenticated)")
                .ConfigureAwait(false);
            return;
        }

        // Poll run state until terminal; heartbeat every pass.
        while (!ct.IsCancellationRequested)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDelegationService>()
                    .HeartbeatAsync(task.Id, ct).ConfigureAwait(false);
            }

            var runs = await _orchestration.GetRunsAsync(issueId, take: 1, ct).ConfigureAwait(false);
            switch (runs.FirstOrDefault()?.State)
            {
                case null:
                case AgentRunState.Queued:
                case AgentRunState.Running:
                    await Task.Delay(RunPollInterval, ct).ConfigureAwait(false);
                    continue;
                case AgentRunState.Succeeded:
                    await FinishAsync(task.Id, ok: true, $"run {runs[0].Id} succeeded").ConfigureAwait(false);
                    return;
                default:
                    await FinishAsync(task.Id, ok: false, $"run {runs[0].Id} {runs[0].State}")
                        .ConfigureAwait(false);
                    return;
            }
        }
    }

    private async Task ExecuteCustomDefAsync(DelegationTaskDto task, CancellationToken ct)
    {
        var found = await CustomCliRunner.FindAsync(_scopeFactory, task.CliName, ct).ConfigureAwait(false);
        if (found is null)
        {
            await FinishAsync(task.Id, ok: false, $"custom cli not found or disabled: {task.CliName}")
                .ConfigureAwait(false);
            return;
        }

        var resolved = CustomCliRunner.ResolveExecutable(found.Executable);
        if (resolved is null)
        {
            await FinishAsync(task.Id, ok: false, $"executable not found: {found.Executable}")
                .ConfigureAwait(false);
            return;
        }

        var workdir = await ResolveWorkdirAsync(task, ct).ConfigureAwait(false);
        var invocation = CustomCliRunner.BuildInvocation(found, task.Prompt, model: null);
        var result = await ChatProcessRunner.RunAsync(
            resolved, invocation.Argv, workdir, CustomCliTimeout, ct, invocation.Stdin)
            .ConfigureAwait(false);

        if (result.TimedOut)
        {
            await FinishAsync(task.Id, ok: false, $"timeout ({CustomCliTimeout.TotalSeconds:0}s)")
                .ConfigureAwait(false);
            return;
        }

        if (result.ExitCode == 0)
        {
            var summary = string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr : result.Stdout;
            await FinishAsync(task.Id, ok: true, Truncate(summary)).ConfigureAwait(false);
        }
        else
        {
            var detail = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            await FinishAsync(task.Id, ok: false, $"exit {result.ExitCode}: {Truncate(detail)}")
                .ConfigureAwait(false);
        }
    }

    /// <summary>Worktree tasks run inside their session path; others on the workspace.</summary>
    private async Task<string> ResolveWorkdirAsync(DelegationTaskDto task, CancellationToken ct)
    {
        if (task.WorktreeRunId is { } runId)
        {
            using var scope = _scopeFactory.CreateScope();
            var isolation = scope.ServiceProvider.GetService<IWorkspaceIsolationService>();
            if (isolation is not null)
            {
                var session = await isolation.GetAsync(runId, ct).ConfigureAwait(false);
                if (session is not null)
                {
                    return session.Path;
                }
            }
        }

        return task.WorkspacePath;
    }

    private async Task FinishAsync(string taskId, bool ok, string detail)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var delegation = scope.ServiceProvider.GetRequiredService<IDelegationService>();
            var task = await delegation.FinishRunAsync(
                taskId, ok, _redactor.Redact(detail), CancellationToken.None).ConfigureAwait(false);
            if (task is null)
            {
                _logger.LogWarning("delegated task {TaskId} vanished before finalize", taskId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "delegated task {TaskId} finalize failed", taskId);
        }
    }

    private async Task<string?> TryGetHeadShaAsync(string repoPath, CancellationToken ct)
    {
        var res = await _git.RunAsync(repoPath, ["rev-parse", "HEAD"], GitProbeTimeout, ct)
            .ConfigureAwait(false);
        return res.ExitCode == 0 ? res.StandardOutput.Trim() : null;
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= ResultSummaryMaxLength ? text : text[..ResultSummaryMaxLength] + "…";
    }
}
