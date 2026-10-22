using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Domain.Entities.Automation;
using Taskboard.Harness;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Taskboard.Integrations.Execution;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Automation;

/// <summary>
/// SPEC-20261021-agent-automations RF-003/RF-004: the fire path shared by
/// the hosted dispatcher and the manual <c>POST /automations/{id}/run</c>.
/// Order: resolve target → optional precheck (cwd=repo, 30s) → create a
/// delegation task (worktree provisioned for <c>repo:</c> targets) → record
/// the run row → recompute the next fire. Every failure mode still records
/// a run so history reflects the attempted fire.
/// </summary>
public sealed class AutomationRunner : IAutomationRunner
{
    private const int PrecheckTimeoutSeconds = 30;

    private readonly IRepository<AgentAutomation> _automations;
    private readonly IRepository<AgentAutomationRun> _runs;
    private readonly IDelegationService _delegation;
    private readonly IWorkspaceIsolationService? _isolation;
    private readonly IProcessRunner _processes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutomationRunner> _logger;
    private readonly IAgentLaunchResolver? _launchResolver;
    private readonly IAgentFeedService? _feed;
    private readonly TimeProvider _clock;

    public AutomationRunner(
        IRepository<AgentAutomation> automations,
        IRepository<AgentAutomationRun> runs,
        IDelegationService delegation,
        IProcessRunner processes,
        IConfiguration configuration,
        ILogger<AutomationRunner> logger,
        IWorkspaceIsolationService? isolation = null,
        IAgentLaunchResolver? launchResolver = null,
        IAgentFeedService? feed = null,
        TimeProvider? clock = null)
    {
        _automations = automations;
        _runs = runs;
        _delegation = delegation;
        _processes = processes;
        _configuration = configuration;
        _logger = logger;
        _isolation = isolation;
        _launchResolver = launchResolver;
        _feed = feed;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public async Task<AgentAutomationRunDto> FireAsync(string automationId, CancellationToken ct = default)
    {
        var auto = await _automations.GetAsync(AgentAutomationId.From(automationId), ct).ConfigureAwait(false)
            ?? throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"Automation '{automationId}' not found.");

        var firedAt = UtcNow;
        var run = AgentAutomationRun.Start(AgentAutomationRunId.NewGuid(), auto.Id, firedAt);

        try
        {
            var resolution = ResolveTarget(auto);
            if (resolution.Error is { } targetError)
            {
                run = AgentAutomationRun.Skipped(run.Id, auto.Id, firedAt, targetError);
                await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
                await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
                return ToDto(run);
            }

            // RF-003.1 — optional precheck gate (cwd=repo, 30s, scrubbed env).
            if (!string.IsNullOrWhiteSpace(auto.Precheck))
            {
                var pre = await _processes.RunAsync(
                    "bash", resolution.WorkspacePath,
                    ["-lc", auto.Precheck],
                    TimeSpan.FromSeconds(PrecheckTimeoutSeconds), ct).ConfigureAwait(false);
                if (pre.ExitCode != 0)
                {
                    run = AgentAutomationRun.Skipped(
                        run.Id, auto.Id, firedAt,
                        $"precheck exit {pre.ExitCode}{(pre.TimedOut ? " (timeout)" : "")}");
                    await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
                    await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
                    return ToDto(run);
                }
            }

            // RF-005 — whitelisted extras: model/effort → launch prefs,
            // --add-dir pairs → verbatim argv pass-through.
            string? model = null, effort = null;
            IReadOnlyList<string> passthrough = [];
            if (!string.IsNullOrWhiteSpace(auto.ExtraAgentArgs))
            {
                var parsed = AgentExtraArgs.Parse(auto.ExtraAgentArgs, auto.AgentCli);
                if (parsed.Error is { } argError)
                {
                    run = AgentAutomationRun.Skipped(run.Id, auto.Id, firedAt, argError);
                    await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
                    await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
                    return ToDto(run);
                }
                model = parsed.Model;
                effort = parsed.Effort;
                passthrough = AgentExtraArgs.PassthroughArgv(parsed);
            }

            if (_launchResolver is not null && (model is not null || effort is not null))
            {
                try
                {
                    var launch = await _launchResolver
                        .ResolveLaunchAsync(auto.AgentCli, model, effort, ct).ConfigureAwait(false);
                    model = launch.EffectiveModel;
                    effort = launch.EffectiveEffort;
                }
                catch (DomainException ex)
                {
                    run = AgentAutomationRun.Skipped(run.Id, auto.Id, firedAt, ex.Message);
                    await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
                    await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
                    return ToDto(run);
                }
            }

            // RF-003.2 — ReuseSession: the last succeeded run's live worktree
            // session is rebound instead of provisioning a fresh one — the
            // worker lands on the same checkout/branch and sees prior work.
            WorktreeSessionDto? reused = null;
            if (auto.ReuseSession && resolution.UseWorktree)
            {
                reused = await FindReusableSessionAsync(auto, ct).ConfigureAwait(false);
            }

            var scope = $"automation:{auto.Id.Value}";
            var task = await _delegation.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    auto.Prompt,
                    auto.AgentCli,
                    scope,
                    reused?.Path ?? resolution.WorkspacePath,
                    DependsOn: null,
                    RetryOf: null,
                    FanoutGroupId: null,
                    UseWorktree: resolution.UseWorktree && reused is null,
                    RepositoryPath: reused?.RepositoryPath ?? resolution.RepositoryPath,
                    BaseCommitSha: null,
                    Kind: "automation",
                    RunId: null,
                    Model: model,
                    Effort: effort,
                    ExtraArgs: passthrough),
                ct).ConfigureAwait(false);

            string? sessionRunId = null;
            if (reused is not null)
            {
                var attached = await _delegation.AttachWorktreeAsync(
                    task.Id, reused.RunId, reused.SharedPathsApplied,
                    reused.SetupState, reused.SetupLogTail, ct).ConfigureAwait(false);
                if (attached is not null)
                {
                    task = attached;
                }
                sessionRunId = reused.RunId;
            }
            else if (resolution.UseWorktree && resolution.RepositoryPath is { } repoPath)
            {
                var provision = await DelegationToolSupport.ProvisionAndAttachAsync(
                    _isolation, _delegation, task, repoPath,
                    _configuration["Git:MainBranch"] ?? "main",
                    "auto", ct).ConfigureAwait(false);
                sessionRunId = provision.Session?.RunId;
                if (provision.Error is not null)
                {
                    run.Bind(task.Id, null);
                    run.Finish(false, UtcNow, skipReason: provision.Error);
                    await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
                    await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
                    return ToDto(run);
                }
            }

            run.Bind(task.Id, sessionRunId);
            await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "automation {Name} ({Id}) fired → task {TaskId}{Reuse}",
                auto.Name, auto.Id.Value, task.Id, reused is not null ? " (reused session)" : "");
            await EmitFeedAsync(auto, run, ct).ConfigureAwait(false);
            return ToDto(run);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "automation {Id} fire failed", automationId);
            run.Finish(false, UtcNow, skipReason: ex.Message);
            try
            {
                await PersistAsync(run, auto, firedAt, ct).ConfigureAwait(false);
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "automation {Id} run persist failed", automationId);
            }
            return ToDto(run);
        }
    }

    /// <summary>Persists the run + advances the automation's next fire.</summary>
    private async Task PersistAsync(
        AgentAutomationRun run, AgentAutomation auto, DateTime firedAt, CancellationToken ct)
    {
        await _runs.AddAsync(run, ct).ConfigureAwait(false);
        auto.MarkFired(firedAt, RecomputeNext(auto, firedAt));
        await _automations.UpdateAsync(auto, ct).ConfigureAwait(false);
        await _automations.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// RF-003.2 reuse lookup: the newest <c>succeeded</c> run bound to a
    /// worktree session that is still <c>Active</c> on disk. Returns null on
    /// any miss — the caller then provisions a fresh session.
    /// </summary>
    private async Task<WorktreeSessionDto?> FindReusableSessionAsync(
        AgentAutomation auto, CancellationToken ct)
    {
        if (_isolation is null)
        {
            return null;
        }

        var lastRun = await _runs.Query
            .Where(r => r.AutomationId == auto.Id
                && r.SessionId != null
                && r.Outcome == AutomationRunOutcomes.Succeeded)
            .OrderByDescending(r => r.FiredAtUtc)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (lastRun?.SessionId is not { } sessionId)
        {
            return null;
        }

        var session = await _isolation.GetAsync(sessionId, ct).ConfigureAwait(false);
        return session is not null
            && string.Equals(session.Status, nameof(WorktreeStatus.Active), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(session.Path)
            ? session
            : null;
    }

    /// <summary>
    /// RF-008 feed emission — <c>AutomationRun</c> item on every fired and
    /// skipped run; failures surface through the run close pass instead.
    /// Feed errors never break the fire path.
    /// </summary>
    private async Task EmitFeedAsync(AgentAutomation auto, AgentAutomationRun run, CancellationToken ct)
    {
        if (_feed is null)
        {
            return;
        }

        try
        {
            var skipped = run.Outcome == AutomationRunOutcomes.Skipped;
            await _feed.ProduceAsync(
                new FeedItemRequest(
                    AgentFeedKind.AutomationRun,
                    skipped
                        ? $"automation '{auto.Name}' skipped"
                        : $"automation '{auto.Name}' fired",
                    run.SkipReason,
                    $"/automations/{auto.Id.Value}",
                    run.SessionId,
                    auto.AgentCli,
                    null,
                    skipped,
                    auto.CreatedBy ?? "local",
                    run.FiredAtUtc),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "automation feed emit failed for {Id}", auto.Id.Value);
        }
    }

    /// <summary>Next cron fire after <paramref name="fromUtc"/> — null for one-shot rows.</summary>
    internal DateTime? RecomputeNext(AgentAutomation auto, DateTime fromUtc)
    {
        if (auto.ScheduleKind != AutomationScheduleKinds.Cron
            || string.IsNullOrWhiteSpace(auto.CronExpression))
        {
            return null;
        }

        var tz = auto.TimeZoneId is { } id
            ? TimeZoneInfo.FindSystemTimeZoneById(id)
            : TimeZoneInfo.Utc;
        return ChatCronSchedule.Parse(auto.CronExpression).NextAfter(fromUtc, tz);
    }

    private (string WorkspacePath, string? RepositoryPath, bool UseWorktree, string? Error)
        ResolveTarget(AgentAutomation auto)
    {
        var workspaceRoot = _configuration["Taskboard:WorkspaceRoot"] ?? "";
        var parts = auto.Target.Split(':', 2);
        var kind = parts[0];
        var value = parts.Length == 2 ? parts[1] : "";

        switch (kind)
        {
            case "repo":
                var repoPath = Path.Join(workspaceRoot, value);
                if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(repoPath))
                {
                    return ("", null, false, $"invalid_target: repo '{value}' not found under workspace root");
                }
                return (repoPath, repoPath, true, null);

            case "worktree":
                if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) || !Directory.Exists(value))
                {
                    return ("", null, false, $"invalid_target: worktree path '{value}' must exist");
                }
                return (value, value, false, null);

            default:
                // project: resolution lands with SPEC-20261021-project-groups.
                return ("", null, false, $"invalid_target: '{kind}:' targets not supported yet");
        }
    }

    private static AgentAutomationRunDto ToDto(AgentAutomationRun r) => new(
        r.Id.Value, r.AutomationId.Value, r.FiredAtUtc, r.Outcome,
        r.ExitCode, r.TaskId, r.SessionId, r.SkipReason, r.DurationMs,
        r.FinishedAtUtc);
}
