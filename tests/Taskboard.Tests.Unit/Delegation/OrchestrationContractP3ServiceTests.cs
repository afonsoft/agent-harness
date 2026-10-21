using NSubstitute;
using Shouldly;
using Taskboard;
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
    private readonly DelegationServiceStubs _s = new();
    private DelegationService Service => _s.Service;

    // ---- RF-006: show/read ----

    [Fact]
    public async Task Dado_DispatchExistente_Quando_Show_Entao_RetornaDispatch()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto());

        var result = await Service.GetDispatchAsync("disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Settled);
    }

    [Fact]
    public async Task Dado_ArquivoCom50Linhas_Quando_ReadComLimit10_Entao_RetornaUltimas10()
    {
        var output = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"linha {i}"));
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(output: output));

        var result = await Service.ReadDispatchOutputAsync("disp-1", lineLimit: 10);

        result.ShouldNotBeNull();
        result.ShouldBe(string.Join('\n', Enumerable.Range(41, 10).Select(i => $"linha {i}")));
    }

    [Fact]
    public async Task Dado_DispatchSemOutput_Quando_Read_Entao_Null()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(output: null));

        var result = await Service.ReadDispatchOutputAsync("disp-1");

        result.ShouldBeNull();
    }

    // ---- RF-006: release/retain/abandon ----

    [Fact]
    public async Task Dado_DispatchSettled_Quando_ReleaseComOutput_Entao_ArquivaELimpaActive()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto());
        _s.Runs.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto());
        _s.Dispatches.TransitionAsync(
                "disp-1", DispatchState.Released, "log final", Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(
                state: DispatchState.Released, output: "log final"));
        _s.Tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.TaskDto(
                "task-1", DelegationTaskStatus.Running, activeDispatchId: "disp-1"));

        var result = await Service.ReleaseDispatchAsync("conv-1", "disp-1", "log final");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Released);
        await _s.Tasks.Received(1).ClearActiveDispatchAsync("task-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DispatchDeOutroScope_Quando_Release_Entao_Null()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto());
        _s.Runs.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto(scope: "outro"));

        var result = await Service.ReleaseDispatchAsync("conv-1", "disp-1");

        result.ShouldBeNull();
        await _s.Dispatches.DidNotReceiveWithAnyArgs()
            .TransitionAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task Dado_DispatchSettled_Quando_Retain_Entao_Retained()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto());
        _s.Runs.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto());
        _s.Dispatches.TransitionAsync(
                "disp-1", DispatchState.Retained, null, Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(state: DispatchState.Retained));

        var result = await Service.RetainDispatchAsync("conv-1", "disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Retained);
    }

    [Fact]
    public async Task Dado_DispatchDispatched_Quando_Abandon_Entao_AbandonedELimpaActive()
    {
        _s.Dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(state: DispatchState.Dispatched));
        _s.Runs.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto());
        _s.Dispatches.TransitionAsync(
                "disp-1", DispatchState.Abandoned, null, Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.DispatchDto(state: DispatchState.Abandoned));
        _s.Tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.TaskDto(
                "task-1", DelegationTaskStatus.Running, activeDispatchId: "disp-1"));

        var result = await Service.AbandonDispatchAsync("conv-1", "disp-1");

        result.ShouldNotBeNull();
        result.State.ShouldBe(DispatchState.Abandoned);
        await _s.Tasks.Received(1).ClearActiveDispatchAsync("task-1", Arg.Any<CancellationToken>());
    }

    // ---- RF-010: reset ----

    [Fact]
    public async Task Dado_RunComDispatchAberto_Quando_ResetSemForce_Entao_Recusa()
    {
        _s.Runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto());
        _s.Dispatches.ListOpenByRunAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([DelegationServiceStubs.DispatchDto(state: DispatchState.Dispatched)]);

        var ex = await Should.ThrowAsync<DomainException>(
            () => Service.ResetRunAsync("conv-1", tasks: true, messages: false));

        ex.Message.ShouldContain("--force");
        await _s.Tasks.DidNotReceiveWithAnyArgs().DeleteByScopeAsync(default!, default);
    }

    [Fact]
    public async Task Dado_RunComDispatchAberto_Quando_ResetComForce_Entao_Apaga()
    {
        _s.Runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>())
            .Returns(DelegationServiceStubs.RunDto());
        _s.Tasks.DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(3);
        _s.Mailbox.DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(7);

        var result = await Service.ResetRunAsync("conv-1", tasks: true, messages: true, force: true);

        result.TasksDeleted.ShouldBe(3);
        result.MessagesDeleted.ShouldBe(7);
        await _s.Dispatches.Received(1).DeleteByRunAsync("run-1", Arg.Any<CancellationToken>());
        await _s.Deliveries.Received(1).DeleteByRunAsync("run-1", Arg.Any<CancellationToken>());
        await _s.Gates.Received(1).DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>());
        await _s.Questions.Received(1).DeleteByScopeAsync("conv-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NenhumFlag_Quando_Reset_Entao_Recusa()
    {
        var ex = await Should.ThrowAsync<DomainException>(
            () => Service.ResetRunAsync("conv-1", tasks: false, messages: false));

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
