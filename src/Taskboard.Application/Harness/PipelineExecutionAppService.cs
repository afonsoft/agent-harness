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

    public PipelineExecutionAppService(
        IRepository<PipelineExecution> executions, PipelineEngine engine)
    {
        _executions = executions;
        _engine = engine;
    }

    public Task<IReadOnlyList<PipelineTemplateDto>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PipelineTemplateDto>>(
            PipelineTemplates.All
                .Select(t => new PipelineTemplateDto(
                    t.TemplateId, t.Name, t.Stages.Select(s => s.Key).ToList()))
                .ToList());

    public async Task<PipelineExecutionDto> StartAsync(
        PipelineStartRequest request, CancellationToken cancellationToken = default)
    {
        var definition = PipelineTemplates.Find(request.TemplateId)
            ?? throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineDag,
                $"Unknown pipeline template '{request.TemplateId}'.");

        var execution = PipelineExecution.Create(
            definition, request.RepositoryFullName, request.RepositoryPath,
            request.BaseBranch, request.IssueId, request.InitialPrompt, DateTime.UtcNow);
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
