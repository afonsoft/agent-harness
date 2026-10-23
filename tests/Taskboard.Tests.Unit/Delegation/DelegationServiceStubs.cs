using NSubstitute;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2: substitute set + service instance
/// shared by the orchestration-contract service tests (P1/P2/P3).
/// </summary>
internal sealed class DelegationServiceStubs
{
    public IDelegationTaskRepository Tasks { get; } = Substitute.For<IDelegationTaskRepository>();
    public IAgentMailboxRepository Mailbox { get; } = Substitute.For<IAgentMailboxRepository>();
    public IDelegationRunRepository Runs { get; } = Substitute.For<IDelegationRunRepository>();
    public IDispatchRepository Dispatches { get; } = Substitute.For<IDispatchRepository>();
    public IDeliveryRepository Deliveries { get; } = Substitute.For<IDeliveryRepository>();
    public IPendingQuestionRepository Questions { get; } = Substitute.For<IPendingQuestionRepository>();
    public IDelegationGateRepository Gates { get; } = Substitute.For<IDelegationGateRepository>();

    public DelegationService Service => new(
        Tasks, Mailbox, Runs, Dispatches, Deliveries,
        Questions, Gates,
            TaskPromptRefinerTestSupport.ConfigOff(),
            Substitute.For<ITaskPromptRefiner>(),
            TimeProvider.System);

    public static DelegationRunDto RunDto(string scope = "conv-1") =>
        new("run-1", scope, "obj", "assistant", DelegationRunStatus.Open, DateTime.UtcNow, null);

    public static DelegationTaskDto TaskDto(
        string id, DelegationTaskStatus status = DelegationTaskStatus.Pending,
        string? activeDispatchId = null) =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null, RunId: "run-1",
            ActiveDispatchId: activeDispatchId);

    public static DispatchDto DispatchDto(
        string id = "disp-1", DispatchState state = DispatchState.Settled,
        string taskId = "task-1", int attempt = 1, string? output = null) =>
        new(id, taskId, "run-1", "codex", attempt, state, "{}", null, output,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null);
}
