using System.Text.Json;
using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 (P3): delegate_worker_{show,read,
/// release,retain,abandon} e delegate_reset.
/// </summary>
public class OrchestrationContractP3ToolsTests
{
    private static ChatToolContext Ctx() =>
        new(
            WorkspacePath: Path.GetTempPath(),
            ProviderId: Guid.NewGuid(),
            ProviderBaseUrl: "http://provider.test",
            ProviderApiKey: "sk",
            ImageModel: "",
            SearchBackend: "none",
            SearchUrl: "",
            SearchApiKey: "",
            ConversationId: "conv-1",
            Model: "m1",
            DelegationDepth: 0,
            Activity: null,
            ToolSet: null,
            DefaultAgentCli: "codex",
            DefaultAgentModel: null);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static DispatchDto Dispatch(
        string id = "disp-1", DispatchState state = DispatchState.Settled) =>
        new(id, "task-1", "run-1", "codex", 1, state, "{}", null, null,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null);

    [Fact]
    public async Task Dado_DispatchExistente_Quando_WorkerShow_Entao_DevolveEstado()
    {
        var service = Substitute.For<IDelegationService>();
        service.GetDispatchAsync("disp-1", Arg.Any<CancellationToken>()).Returns(Dispatch());
        var tool = new DelegateWorkerShowTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("disp-1");
        result.Json.ShouldContain("settled");
    }

    [Fact]
    public async Task Dado_DispatchInexistente_Quando_WorkerShow_Entao_Erro()
    {
        var service = Substitute.For<IDelegationService>();
        var tool = new DelegateWorkerShowTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-x"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldBe("dispatch not found");
    }

    [Fact]
    public async Task Dado_OutputArquivado_Quando_WorkerRead_Entao_DevolveOutput()
    {
        var service = Substitute.For<IDelegationService>();
        service.ReadDispatchOutputAsync("disp-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("linha1\nlinha2");
        var tool = new DelegateWorkerReadTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1","limit":50}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("linha2");
    }

    [Fact]
    public async Task Dado_SemOutput_Quando_WorkerRead_Entao_Erro()
    {
        var service = Substitute.For<IDelegationService>();
        service.ReadDispatchOutputAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var tool = new DelegateWorkerReadTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldBe("output not found");
    }

    [Fact]
    public async Task Dado_ReleaseOk_Quando_WorkerRelease_Entao_EstadoReleased()
    {
        var service = Substitute.For<IDelegationService>();
        service.ReleaseDispatchAsync("conv-1", "disp-1", "log", Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Released));
        var tool = new DelegateWorkerReleaseTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(
            Args("""{"dispatch":"disp-1","output":"log"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("released");
    }

    [Fact]
    public async Task Dado_TransicaoInvalida_Quando_WorkerRelease_Entao_Recusado()
    {
        var service = Substitute.For<IDelegationService>();
        service.ReleaseDispatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<DispatchDto?>(_ => throw new DomainException("INVALID", "only settled|retained"));
        var tool = new DelegateWorkerReleaseTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldBe("release refused");
    }

    [Fact]
    public async Task Dado_DispatchSettled_Quando_WorkerRetain_Entao_EstadoRetained()
    {
        var service = Substitute.For<IDelegationService>();
        service.RetainDispatchAsync("conv-1", "disp-1", Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Retained));
        var tool = new DelegateWorkerRetainTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("retained");
    }

    [Fact]
    public async Task Dado_Dispatch_Quando_WorkerAbandon_Entao_EstadoAbandoned()
    {
        var service = Substitute.For<IDelegationService>();
        service.AbandonDispatchAsync("conv-1", "disp-1", Arg.Any<CancellationToken>())
            .Returns(Dispatch(state: DispatchState.Abandoned));
        var tool = new DelegateWorkerAbandonTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"dispatch":"disp-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("abandoned");
    }

    // ---- RF-008: delegate_task launch receipt ----

    [Fact]
    public async Task Dado_ModelEEffort_Quando_DelegateTask_Entao_ReceiptTemLaunch()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = (CreateDelegationTaskRequest)ci[0];
                req.Model.ShouldBe("gpt-5");
                req.Effort.ShouldBe("high");
                return new DelegationTaskDto(
                    "task-1", req.Scope, req.Prompt, req.CliName, [], null, null,
                    false, null, "/ws", null, null, DelegationTaskStatus.Pending,
                    null, null, DateTime.UtcNow, null, null, null,
                    RunId: "run-1", Model: req.Model, Effort: req.Effort);
            });
        var resolver = Substitute.For<IAgentLaunchResolver>();
        resolver.ResolveLaunchAsync("codex", "gpt-5", "high", Arg.Any<CancellationToken>())
            .Returns(new LaunchResolution("gpt-5", "ultra", []));
        var tool = new DelegateTaskTool(
            TestScopeFactory.WithDelegationAndLaunch(service, resolver),
            Substitute.For<Taskboard.Integrations.Harness.IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"faça","cli":"codex","model":"gpt-5","effort":"high"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"model\":\"gpt-5\"");
        result.Json.ShouldContain("\"effort\":\"ultra\"");
    }

    [Fact]
    public async Task Dado_PrefNaoSuportada_Quando_DelegateTask_Entao_Recusado()
    {
        var service = Substitute.For<IDelegationService>();
        var resolver = Substitute.For<IAgentLaunchResolver>();
        resolver.ResolveLaunchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<LaunchResolution>(_ => throw new DomainException("INVALID", "launch_preference_unsupported"));
        var tool = new DelegateTaskTool(
            TestScopeFactory.WithDelegationAndLaunch(service, resolver),
            Substitute.For<Taskboard.Integrations.Harness.IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"faça","cli":"codex","effort":"turbo"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await service.DidNotReceiveWithAnyArgs()
            .CreateTaskAsync(default!, default);
    }

    [Fact]
    public async Task Dado_AllFlag_Quando_Reset_Entao_ApagouTasksEMessages()
    {
        var service = Substitute.For<IDelegationService>();
        service.ResetRunAsync("conv-1", true, true, false, Arg.Any<CancellationToken>())
            .Returns(new ResetRunResult(4, 9));
        var tool = new DelegateResetTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"all":true}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("4");
        result.Json.ShouldContain("9");
        await service.Received(1).ResetRunAsync(
            "conv-1", true, true, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ResetRecusado_Quando_DispatchAberto_Entao_Erro()
    {
        var service = Substitute.For<IDelegationService>();
        service.ResetRunAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<ResetRunResult>(_ => throw new DomainException("INVALID", "pass --force"));
        var tool = new DelegateResetTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"tasks":true}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldBe("reset refused");
    }
}
