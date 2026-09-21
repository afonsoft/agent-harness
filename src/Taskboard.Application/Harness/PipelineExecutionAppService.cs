using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
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
    private readonly ICockpitEventStream? _cockpit;

    public PipelineExecutionAppService(
        IRepository<PipelineExecution> executions,
        PipelineEngine engine,
        ICockpitEventStream? cockpit = null)
    {
        _executions = executions;
        _engine = engine;
        _cockpit = cockpit;
    }

    public Task<IReadOnlyList<PipelineTemplateDto>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PipelineTemplateDto>>(
            PipelineTemplates.All
                .Select(t => new PipelineTemplateDto(
                    t.TemplateId, t.Name, t.Stages.Select(s => s.Key).ToList()))
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
        if (request.TemplateId == PipelineTemplates.SingleAgentId)
        {
            definition = ApplyOverrides(definition, request);
        }

        var execution = PipelineExecution.Create(
            definition, request.RepositoryFullName, request.RepositoryPath,
            request.BaseBranch, request.IssueId, request.InitialPrompt, DateTime.UtcNow,
            request.MaxBudgetUsd);
        await _executions.AddAsync(execution, cancellationToken).ConfigureAwait(false);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(execution.Id.Value, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto?> GetAsync(
        string pipelineExecutionId, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        return execution is null ? null : ToDto(execution);
    }

    public async Task<PipelineExecutionDto> ApproveStageAsync(
        string pipelineExecutionId, string stageKey, string? comment,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.ApproveStage(stageKey, comment, DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto> RejectStageAsync(
        string pipelineExecutionId, string stageKey, string? comment,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.FailStage(stageKey, $"Rejected by reviewer{(string.IsNullOrWhiteSpace(comment) ? "." : $": {comment}")}", DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PipelineExecutionDto> RetryStageAsync(
        string pipelineExecutionId, string stageKey, string? adjustedPrompt,
        CancellationToken cancellationToken = default)
    {
        var execution = await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false);
        execution.RetryStage(stageKey, adjustedPrompt, DateTime.UtcNow);
        await _executions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        await PublishStatusAsync(pipelineExecutionId, "Run paused").ConfigureAwait(false);
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
        await PublishStatusAsync(pipelineExecutionId, "Run resumed").ConfigureAwait(false);
        await _engine.DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(await LoadAsync(pipelineExecutionId, cancellationToken).ConfigureAwait(false));
    }

    private Task PublishStatusAsync(string runId, string title) =>
        _cockpit?.PublishAsync(new CockpitEventDto(runId, DateTimeOffset.UtcNow, "status", title, null))
            ?? Task.CompletedTask;

    /// <summary>
    /// `single-agent` only — rewrites the AgentWork stage with the requested
    /// agent/tier and drops Verification when skipped
    /// (SPEC-20260920-board-cockpit-unified-runs R2).
    /// </summary>
    private static PipelineDefinition ApplyOverrides(
        PipelineDefinition definition, PipelineStartRequest request) =>
        definition with
        {
            Stages = definition.Stages
                .Where(s => !(request.SkipVerification && s.Kind is PipelineStageKind.Verification))
                .Select(s => s.Kind is PipelineStageKind.AgentWork
                    ? s with
                    {
                        Agent = request.AgentOverride ?? s.Agent,
                        ModelTier = request.TierOverride ?? s.ModelTier
                    }
                    : s)
                .ToList()
        };

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
                    s.DependsOn))
                .ToList());
}
