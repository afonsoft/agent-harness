using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Harness;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// <see cref="IPipelineOrchestrator"/> implementation — thin coordination layer
/// over the domain aggregate and <see cref="PipelineEngine"/>
/// (SPEC-20260919-ade-multi-agent-orchestration §5).
/// </summary>
public sealed class PipelineExecutionAppService : IPipelineOrchestrator
{
    private readonly IRepository<PipelineExecution> _executions;
    private readonly PipelineEngine _engine;
    private readonly IWorkspaceIsolationService _isolation;
    private readonly IGitHubService _gitHub;
    private readonly ILogger<PipelineExecutionAppService> _logger;
    private readonly IAgentEligibilityService? _eligibility;
    private readonly ICockpitEventStream? _cockpit;
    private readonly IRepositoryProvisioningService? _provisioning;
    private readonly IAgentExecutionEventSink? _eventSink;

    public PipelineExecutionAppService(
        IRepository<PipelineExecution> executions,
        PipelineEngine engine,
        IWorkspaceIsolationService isolation,
        IGitHubService gitHub,
        ILogger<PipelineExecutionAppService> logger,
        IAgentEligibilityService? eligibility = null,
        ICockpitEventStream? cockpit = null,
        IRepositoryProvisioningService? provisioning = null,
        IAgentExecutionEventSink? eventSink = null)
    {
        _executions = executions;
        _engine = engine;
        _isolation = isolation;
        _gitHub = gitHub;
        _logger = logger;
        _eligibility = eligibility;
        _cockpit = cockpit;
        _provisioning = provisioning;
        _eventSink = eventSink;
    }

    public Task<IReadOnlyList<PipelineTemplateDto>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PipelineTemplateDto>>(
            PipelineTemplates.All
                .Select(t => new PipelineTemplateDto(
                    t.TemplateId,
                    t.Name,
                    t.Stages.Select(s => s.Key).ToList(),
                    t.Stages.Select(s => new PipelineTemplateStageDto(
                        s.Key,
                        s.Name,
                        s.Kind.ToString(),
                        s.Role?.ToString(),
                        s.Agent?.ToString(),
                        s.ModelTier.ToString())).ToList()))
                .ToList());

    public async Task<IReadOnlyList<PipelineExecutionDto>> ListAsync(
        int take = 50, CancellationToken cancellationToken = default) =>
        await _executions.Query
            .Include(e => e.Stages)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(take)
            .Select(e => ToDto(e))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<PipelineExecutionDto> StartAsync(
        PipelineStartRequest request, CancellationToken cancellationToken = default)
    {
        var hasOverrides = request.AgentOverride is not null
            || request.TierOverride is not null
            || request.SkipVerification;
        if (hasOverrides && request.TemplateId != PipelineTemplates.SingleAgentId)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Agent/tier/verification overrides are only supported by the 'single-agent' template.");
        }

        var definition = PipelineTemplates.Find(request.TemplateId)
            ?? throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineDag,
                $"Unknown pipeline template '{request.TemplateId}'.");
        if (request.TemplateId == PipelineTemplates.SingleAgentId && request.SkipVerification)
        {
            definition = definition with
            {
                Stages = definition.Stages
                    .Where(s => s.Kind is not PipelineStageKind.Verification)
                    .ToList()
            };
        }

        // SPEC-20260922-cockpit-agent-selection-fallback RF-001/RF-002: apply
        // per-stage/single-agent picks to any template, then bind every
        // AgentWork stage to an eligible CLI before the run is created.
        definition = await ApplyStageOverridesAsync(definition, request, cancellationToken).ConfigureAwait(false);

        // SPEC-20260923-cockpit-run-hardening RF-001: the repository path the
        // worktree is cut from always comes from the provisioning service —
        // ~/repos/<repo-name> — never a client-supplied or root-fallback path.
        var repositoryPath = _provisioning?.ResolveClonePath(request.RepositoryFullName)
            ?? request.RepositoryPath;
        if (_provisioning is not null
            && !string.IsNullOrWhiteSpace(request.RepositoryPath)
            && !string.Equals(request.RepositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Repository path override '{Requested}' ignored — run uses the provisioned clone '{Resolved}'.",
                request.RepositoryPath, repositoryPath);
        }

        var execution = PipelineExecution.Create(
            definition, request.RepositoryFullName, repositoryPath,
            request.BaseBranch, request.IssueId, request.InitialPrompt, DateTime.UtcNow,
            request.MaxBudgetUsd);
        await _executions.AddAsync(execution, cancellationToken).ConfigureAwait(false);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_provisioning is not null)
        {
            EmitRunEvent(execution, AgentEventKinds.Lifecycle,
                $"Provisioning repository clone for {request.RepositoryFullName}",
                new { repository = request.RepositoryFullName, path = repositoryPath });
            try
            {
                var clone = await _provisioning.EnsureCloneAsync(
                        request.RepositoryFullName, cancellationToken)
                    .ConfigureAwait(false);
                EmitRunEvent(execution, AgentEventKinds.Lifecycle,
                    clone.Cloned
                        ? $"Repository cloned to {clone.Path} in {clone.ElapsedMs}ms"
                        : $"Reusing repository clone at {clone.Path}",
                    new { repository = request.RepositoryFullName, clone.Path, clone.Cloned, clone.ElapsedMs });
            }
            catch (Exception ex)
            {
                var message = $"Repository provisioning failed: {ex.Message}";
                EmitRunEvent(execution, AgentEventKinds.Error, message,
                    new { repository = request.RepositoryFullName, path = repositoryPath, error = ex.Message });
                execution.Fail(message, DateTime.UtcNow);
                await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (ex is DomainException)
                {
                    throw;
                }

                throw new DomainException(
                    TaskboardDomainErrorCodes.RepositoryProvisioningFailed, message);
            }
        }

        await PublishStatusAsync(execution.Id.Value, "Run created", execution.Status)
            .ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(execution.Id.Value, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto?> GetAsync(
        string pipelineExecutionId, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        return execution is null ? null : ToDto(execution);
    }

    public async Task<PipelineExecutionDto?> GetLatestByIssueAsync(
        string issueId, CancellationToken cancellationToken = default)
    {
        var execution = await _executions.Query
            .Include(e => e.Stages)
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return execution is null ? null : ToDto(execution);
    }

    public async Task<PipelineExecutionDto> ApproveStageAsync(
        string pipelineExecutionId, string stageKey, string? comment,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.ApproveStage(stageKey, comment, DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto> RejectStageAsync(
        string pipelineExecutionId, string stageKey, string? comment,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        var reason = $"Rejected by reviewer{(string.IsNullOrWhiteSpace(comment) ? "." : $": {comment}")}";
        execution.FailStage(stageKey, reason, DateTime.UtcNow);
        // RF-002 failure policy: rejecting a gate is a terminal decision —
        // the run stops instead of looping the approval back through retries.
        execution.Fail($"Stage '{stageKey}' {reason[..1].ToLowerInvariant()}{reason[1..]}", DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        return ToDto(execution);
    }

    public async Task<PipelineExecutionDto> RetryStageAsync(
        string pipelineExecutionId, string stageKey, string? adjustedPrompt,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.RetryStage(stageKey, adjustedPrompt, DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto> CancelAsync(
        string pipelineExecutionId, CancellationToken cancellationToken = default)
    {
        _engine.CancelExecution(pipelineExecutionId);
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.Cancel(DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        return ToDto(execution);
    }

    public async Task<PipelineExecutionDto?> PauseAsync(
        string pipelineExecutionId, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return null;
        }

        execution.Pause();
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        return ToDto(execution);
    }

    public async Task<PipelineExecutionDto?> ResumeAsync(
        string pipelineExecutionId, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return null;
        }

        execution.Resume();
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishRunStatusAsync(execution).ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<string?> CreatePullRequestAsync(
        string pipelineExecutionId, string title, string? body, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return null;
        }

        if (execution.Status != PipelineStatus.Completed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineState,
                $"Run is {execution.Status} — a PR can only be created once the pipeline completes.");
        }

        var session = await _isolation.GetAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineState,
                "Run has no worktree — nothing to push.");
        }

        // RF-005: commit pending changes, push the worktree branch, open the PR.
        var diff = await _isolation.GetDiffAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        if (diff.FilesChanged > 0)
        {
            await _isolation.CommitAsync(pipelineExecutionId, title, "Harness <harness@taskboard.local>", cancellationToken)
                .ConfigureAwait(false);
        }

        var branch = await _isolation.PushAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        var prUrl = await _gitHub.CreatePullRequestAsync(
            execution.RepositoryFullName, title, branch, execution.BaseBranch, body, cancellationToken)
            .ConfigureAwait(false);

        await PublishIssueReviewAsync(execution, prUrl, cancellationToken).ConfigureAwait(false);
        return prUrl;
    }

    /// <summary>
    /// Board bookkeeping once the PR exists: the bound issue card moves to
    /// <c>in_review</c> and the PR link lands as a comment
    /// (SPEC-20260919-ade-cockpit-hitl RF-005). Best-effort — the PR was
    /// already created, so GitHub board failures never fail the request.
    /// </summary>
    private async Task PublishIssueReviewAsync(
        PipelineExecution execution, string prUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(execution.IssueId))
        {
            return;
        }

        try
        {
            var issue = (await _gitHub.GetIssuesAsync(execution.RepositoryFullName, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(i => i.Id.ToString(CultureInfo.InvariantCulture) == execution.IssueId);
            if (issue is null)
            {
                _logger.LogWarning(
                    "Run {RunId}: issue {IssueId} not found on {Repo} — card not moved to in_review.",
                    execution.Id.Value, execution.IssueId, execution.RepositoryFullName);
                return;
            }

            await _gitHub.UpdateIssueColumnAsync(
                execution.RepositoryFullName, issue.Number, issue.Column,
                GitHubBoardColumn.InReview, cancellationToken).ConfigureAwait(false);
            await _gitHub.AddIssueCommentAsync(
                execution.RepositoryFullName, issue.Number,
                $"Pull request opened by Harness: {prUrl}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Run {RunId}: could not move issue {IssueId} to in_review or comment the PR link.",
                execution.Id.Value, execution.IssueId);
        }
    }

    private Task PublishStatusAsync(string runId, string title, PipelineStatus status) =>
        _cockpit?.PublishAsync(new CockpitEventDto(
            runId, DateTimeOffset.UtcNow, "run_status", title,
            JsonSerializer.Serialize(new { status = status.ToString() }, JsonOptions)))
            ?? Task.CompletedTask;

    /// <summary>RF-003: every execution transition streams <c>run_status</c> to the cockpit.</summary>
    private Task PublishRunStatusAsync(PipelineExecution execution) =>
        PublishStatusAsync(execution.Id.Value, $"Run {execution.Status}", execution.Status);

    /// <summary>
    /// RF-004: durable normalized event on the run scope — mirrored under the
    /// bound issue scope so the Board task log sees provisioning outcomes too.
    /// </summary>
    private void EmitRunEvent(PipelineExecution execution, string kind, string title, object? payload = null)
    {
        if (_eventSink is null)
        {
            return;
        }

        var evt = new AgentExecutionEvent(
            string.Empty, AgentEventScope.Run, execution.Id.Value, 0, DateTimeOffset.UtcNow,
            kind, null,
            Title: title,
            PayloadJson: payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions));
        _ = _eventSink.EmitAsync(evt, CancellationToken.None);
        if (!string.IsNullOrEmpty(execution.IssueId))
        {
            _ = _eventSink.EmitAsync(
                evt with { ScopeKind = AgentEventScope.Issue, ScopeId = execution.IssueId },
                CancellationToken.None);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SPEC-20260922-cockpit-agent-selection-fallback RF-001/RF-002 — binds
    /// every AgentWork stage to a CLI before the run exists:
    /// <list type="number">
    /// <item>explicit picks (<c>StageOverrides</c>, <c>SingleAgentType</c> or
    /// legacy <c>AgentOverride</c> on <c>single-agent</c>) must be eligible —
    /// otherwise <see cref="TaskboardDomainErrorCodes.AgentNotEligible"/> (422);</item>
    /// <item>Auto (no explicit pick) keeps the template default when eligible,
    /// else falls to the first eligible CLI in enum order;</item>
    /// <item>Single Agent with Auto picks the first eligible CLI once and
    /// applies it to every AgentWork stage;</item>
    /// <item>no eligible CLI at all → 422 — a run never dispatches a disabled
    /// or unauthenticated CLI.</item>
    /// </list>
    /// </summary>
    private async Task<PipelineDefinition> ApplyStageOverridesAsync(
        PipelineDefinition definition, PipelineStartRequest request, CancellationToken cancellationToken)
    {
        var agentWork = definition.Stages.Where(s => s.Kind is PipelineStageKind.AgentWork).ToList();
        if (agentWork.Count == 0)
        {
            return definition;
        }

        if (request.StageOverrides is not null)
        {
            foreach (var key in request.StageOverrides.Keys)
            {
                var stage = definition.Stages.FirstOrDefault(s => s.Key == key)
                    ?? throw new DomainException(
                        TaskboardDomainErrorCodes.InvalidValue,
                        $"Unknown stage '{key}' in template '{definition.TemplateId}'.");
                if (stage.Kind is not PipelineStageKind.AgentWork)
                {
                    throw new DomainException(
                        TaskboardDomainErrorCodes.InvalidValue,
                        $"Stage '{key}' is {stage.Kind} — agent overrides only apply to AgentWork stages.");
                }
            }
        }

        var eligible = _eligibility is null
            ? null
            : await _eligibility.GetEligibleTypesAsync(cancellationToken).ConfigureAwait(false);

        // Single-agent Auto resolves once so every stage runs the same CLI.
        AgentType? singleAuto = null;
        if (request.SingleAgent && request.SingleAgentType is null && eligible is not null)
        {
            singleAuto = FirstEligible(eligible)
                ?? throw new DomainException(
                    TaskboardDomainErrorCodes.AgentNotEligible,
                    "No eligible agent CLI — install and authenticate one under Settings → Agents.");
        }

        var legacyAgent = request.TemplateId == PipelineTemplates.SingleAgentId ? request.AgentOverride : null;
        var legacyTier = request.TemplateId == PipelineTemplates.SingleAgentId ? request.TierOverride : null;

        var stages = definition.Stages.Select(s =>
        {
            if (s.Kind is not PipelineStageKind.AgentWork)
            {
                return s;
            }

            var ov = request.StageOverrides?.GetValueOrDefault(s.Key);
            var pick = ov?.Agent ?? (request.SingleAgent ? request.SingleAgentType : null) ?? legacyAgent;
            AgentType? resolved;
            if (pick is { } explicitPick)
            {
                if (eligible is not null && !eligible.Contains(explicitPick))
                {
                    throw new DomainException(
                        TaskboardDomainErrorCodes.AgentNotEligible,
                        $"Stage '{s.Key}' requests '{explicitPick}' but it is not installed, authenticated and enabled — pick an available CLI or leave it on Auto.");
                }

                resolved = explicitPick;
            }
            else if (singleAuto is { } single)
            {
                resolved = single;
            }
            else
            {
                resolved = s.Agent;
                if (eligible is not null && (resolved is null || !eligible.Contains(resolved.Value)))
                {
                    resolved = FirstEligible(eligible)
                        ?? throw new DomainException(
                            TaskboardDomainErrorCodes.AgentNotEligible,
                            $"No eligible agent CLI for stage '{s.Key}' — install and authenticate one under Settings → Agents.");
                }
            }

            var tier = ov?.Tier
                ?? (request.SingleAgent ? request.SingleAgentTier : null)
                ?? legacyTier
                ?? s.ModelTier;
            return s with { Agent = resolved, ModelTier = tier };
        }).ToList();

        return definition with { Stages = stages };
    }

    private static AgentType? FirstEligible(IReadOnlySet<AgentType> eligible)
    {
        foreach (var type in Enum.GetValues<AgentType>())
        {
            if (eligible.Contains(type))
            {
                return type;
            }
        }

        return null;
    }

    private Task<PipelineExecution> LoadAsync(string id, CancellationToken cancellationToken) =>
        _executions.Query
            .Include(e => e.Stages)
            .SingleAsync(e => e.Id == PipelineExecutionId.From(id), cancellationToken);

    private Task<PipelineExecution?> FindAsync(string id, CancellationToken cancellationToken) =>
        _executions.Query
            .Include(e => e.Stages)
            .SingleOrDefaultAsync(e => e.Id == PipelineExecutionId.From(id), cancellationToken);

    private static PipelineExecutionDto ToDto(PipelineExecution execution) =>
        new(
            execution.Id.Value,
            execution.TemplateId,
            execution.RepositoryFullName,
            execution.BaseBranch,
            execution.Status.ToString(),
            execution.WorktreePath,
            execution.CreatedAtUtc,
            execution.CompletedAtUtc,
            execution.Stages
                .Select(s => new PipelineStageDto(
                    s.StageKey,
                    s.Name,
                    s.Kind.ToString(),
                    s.Status.ToString(),
                    s.Role?.ToString(),
                    s.Agent?.ToString(),
                    s.Attempts,
                    s.HandoffSummary,
                    s.LastError,
                    s.DependsOn,
                    s.TriedAgents,
                    s.AutoRetryCount,
                    s.NextAutoRetryAtUtc))
                .ToList(),
            execution.IssueId,
            execution.FailureReason,
            execution.RepositoryPath);
}
