using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 (P3): dispatch lifecycle
/// (RF-006 show/read/release/retain/abandon), launch prefs (RF-008) e
/// delegate_reset (RF-010).
/// </summary>
public class OrchestrationContractP3ServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly IDelegationRunRepository _runs = Substitute.For<IDelegationRunRepository>();
    private readonly IDispatchRepository _dispatches = Substitute.For<IDispatchRepository>();
    private readonly IDeliveryRepository _deliveries = Substitute.For<IDeliveryRepository>();
    private readonly IPendingQuestionRepository _questions = Substitute.For<IPendingQuestionRepository>();
    private readonly IDelegationGateRepository _gates = Substitute.For<IDelegationGateRepository>();
    private readonly DelegationService _service;

    public OrchestrationContractP3ServiceTests()
    {
        _service = new DelegationService(
            _tasks, _mailbox, _runs, _dispatches, _deliveries,
            _questions, _gates, TimeProvider.System);
    }

    private static DelegationRunDto Run(string scope = "conv-1") =>
        new("run-1", scope, "obj", "assistant", DelegationRunStatus.Open, DateTime.UtcNow, null);

    private static DispatchDto Dispatch(
        string id = "disp-1", DispatchState state = DispatchState.Settled,
        string taskId = "task-1", string? output = null) =>
        new(id, taskId, "run-1", "codex", 1, state, "{}", null, output,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null);

    private static DelegationTaskDto Task(string id, string? activeDispatchId = null) =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, DelegationTaskStatus.Running, null, null,
            DateTime.UtcNow, null, null, null, RunId: "run-1",
            ActiveDispatchId: activeDispatchId);

    // ---- RF-006: show/read ----

    [Fact]
    public async Task Dado_DispatchExistente_Quando_Show_Entao_RetornaDispatch()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>()).Returns(Dispatch());

        var result = await _service.GetDispatchAsync("disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Settled);
    }

    [Fact]
    public async Task Dado_ArquivoCom50Linhas_Quando_ReadComLimit10_Entao_RetornaUltimas10()
    {
        var output = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"linha {i}"));
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(Dispatch(output: output));

        var result = await _service.ReadDispatchOutputAsync("disp-1", lineLimit: 10);

        result.ShouldNotBeNull();
        result.ShouldBe(string.Join('\n', Enumerable.Range(41, 10).Select(i => $"linha {i}")));
    }

    [Fact]
    public async Task Dado_DispatchSemOutput_Quando_Read_Entao_Null()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(Dispatch(output: null));

        var result = await _service.ReadDispatchOutputAsync("disp-1");

        result.ShouldBeNull();
    }

    // ---- RF-006: release/retain/abandon ----

    [Fact]
    public async Task Dado_DispatchSettled_Quando_ReleaseComOutput_Entao_ArquivaELimpaActive()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>()).Returns(Dispatch());
        _runs.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(Run());
        _dispatches.TransitionAsync(
                "disp-1", DispatchState.Released, "log final", Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Released, output: "log final"));
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(Task("task-1", activeDispatchId: "disp-1"));

        var result = await _service.ReleaseDispatchAsync("conv-1", "disp-1", "log final");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Released);
        await _tasks.Received(1).ClearActiveDispatchAsync("task-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DispatchDeOutroScope_Quando_Release_Entao_Null()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>()).Returns(Dispatch());
        _runs.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(Run(scope: "outro"));

        var result = await _service.ReleaseDispatchAsync("conv-1", "disp-1");

        result.ShouldBeNull();
        await _dispatches.DidNotReceiveWithAnyArgs()
            .TransitionAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task Dado_DispatchSettled_Quando_Retain_Entao_Retained()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>()).Returns(Dispatch());
        _runs.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(Run());
        _dispatches.TransitionAsync(
                "disp-1", DispatchState.Retained, null, Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Retained));

        var result = await _service.RetainDispatchAsync("conv-1", "disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Retained);
    }

    [Fact]
    public async Task Dado_DispatchDispatched_Quando_Abandon_Entao_AbandonedELimpaActive()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Dispatched));
        _runs.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(Run());
        _dispatches.TransitionAsync(
                "disp-1", DispatchState.Abandoned, null, Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Abandoned));
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(Task("task-1", activeDispatchId: "disp-1"));

        var result = await _service.AbandonDispatchAsync("conv-1", "disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Abandoned);
        await _tasks.Received(1).ClearActiveDispatchAsync("task-1", Arg.Any<CancellationToken>());
    }

    // ---- RF-010: reset ----

    [Fact]
    public async Task Dado_RunComDispatchAberto_Quando_ResetSemForce_Entao_Recusa()
    {
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(Run());
        _dispatches.ListOpenByRunAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([Dispatch(state: DispatchState.Dispatched)]);

        var ex = await Should.ThrowAsync<DomainException>(
            () => _service.ResetRunAsync("conv-1", tasks: true, messages: false));

        ex.Message.ShouldContain("--force");
        await _tasks.DidNotReceiveWithAnyArgs().DeleteByScopeAsync(default!, default);
    }

    [Fact]
    public async Task Dado_RunComDispatchAberto_Quando_ResetComForce_Entao_Apaga()
    {
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(Run());
        _tasks.DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(3);
        _mailbox.DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(7);

        var result = await _service.ResetRunAsync("conv-1", tasks: true, messages: true, force: true);

        result.TasksDeleted.ShouldBe(3);
        result.MessagesDeleted.ShouldBe(7);
        await _dispatches.Received(1).DeleteByRunAsync("run-1", Arg.Any<CancellationToken>());
        await _deliveries.Received(1).DeleteByRunAsync("run-1", Arg.Any<CancellationToken>());
        await _gates.Received(1).DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>());
        await _questions.Received(1).DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NenhumFlag_Quando_Reset_Entao_Recusa()
    {
        var ex = await Should.ThrowAsync<DomainException>(
            () => _service.ResetRunAsync("conv-1", tasks: false, messages: false));

        ex.Message.ShouldContain("nothing to reset");
    }

    // ---- RF-008: launch resolver ----

    [Fact]
    public async Task Dado_BuiltinCli_Quando_ModelEEffort_Entao_Resolve()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var resolver = new AgentLaunchResolver(defs);

        var result = await resolver.ResolveLaunchAsync("codex", "gpt-5", "high");

        result.EffectiveModel.ShouldBe("gpt-5");
        result.EffectiveEffort.ShouldBe("ultra");
    }

    [Fact]
    public async Task Dado_EffortInvalido_Quando_Resolve_Entao_Recusa()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var resolver = new AgentLaunchResolver(defs);

        var ex = await Should.ThrowAsync<DomainException>(
            () => resolver.ResolveLaunchAsync("codex", null, "turbo"));

        ex.Message.ShouldContain("launch_preference_unsupported");
    }

    [Fact]
    public async Task Dado_CustomDefSemModelFlag_Quando_Model_Entao_Recusa()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([
            new AgentCliDefinitionDto(
                "mycli", "My CLI", "mycli", "{prompt}", "proc", null,
                "-v", true, true),
        ]);
        var resolver = new AgentLaunchResolver(defs);

        var ex = await Should.ThrowAsync<DomainException>(
            () => resolver.ResolveLaunchAsync("mycli", "m1", null));

        ex.Message.ShouldContain("launch_preference_unsupported");
    }

    [Fact]
    public async Task Dado_CustomDefComModelFlag_Quando_Effort_Entao_Recusa()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([
            new AgentCliDefinitionDto(
                "mycli", "My CLI", "mycli", "{prompt}", "proc", "--model",
                "-v", true, true),
        ]);
        var resolver = new AgentLaunchResolver(defs);

        var ex = await Should.ThrowAsync<DomainException>(
            () => resolver.ResolveLaunchAsync("mycli", null, "high"));

        ex.Message.ShouldContain("launch_preference_unsupported");
    }

    [Fact]
    public async Task Dado_CliDesconhecido_Quando_Prefs_Entao_Recusa()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        var resolver = new AgentLaunchResolver(defs);

        var ex = await Should.ThrowAsync<DomainException>(
            () => resolver.ResolveLaunchAsync("nope", "m1", null));

        ex.Message.ShouldContain("launch_preference_unsupported");
    }

    [Fact]
    public async Task Dado_SemPrefs_Quando_Resolve_Entao_Noop()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var resolver = new AgentLaunchResolver(defs);

        var result = await resolver.ResolveLaunchAsync("codex", null, null);

        result.EffectiveModel.ShouldBeNull();
        result.EffectiveEffort.ShouldBeNull();
    }
}
