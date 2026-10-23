using System.Text.Json;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2: agent_check (deliveries+ack),
/// delegate_run_show e o receipt de retry do delegate_task.
/// </summary>
public class OrchestrationContractToolsTests
{
    private static ChatToolContext Ctx(string? defaultAgentCli = "codex") =>
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
            DefaultAgentCli: defaultAgentCli,
            DefaultAgentModel: null);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static MailboxMessageDto Msg(string id, string kind = "text") =>
        new(id, "conv-1", "codex", "@all", kind, "body", DateTime.UtcNow, null);

    private static DeliveryDto Delivery(string id = "del-1", DateTime? acked = null) =>
        new(id, "run-1", ["msg-1"], DateTime.UtcNow, acked);

    private static DelegationTaskDto Task(string id, string? runId = "run-1",
        string? activeDispatch = null) =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, DelegationTaskStatus.Pending, null, null,
            DateTime.UtcNow, null, null, null, RunId: runId, ActiveDispatchId: activeDispatch);

    private static DispatchDto Dispatch(string id = "disp-2", int attempt = 2) =>
        new(id, "task-1", "run-1", "codex", attempt, DispatchState.Pending, "{}",
            null, null, DateTime.UtcNow, null, null, null);

    // ---- agent_check ----

    [Fact]
    public async Task Dado_DeliveryAberta_Quando_Check_Entao_RepassaBatchEAck()
    {
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        service.CheckDeliveriesAsync(Arg.Any<CheckDeliveriesRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DeliveryCheckResult(
                Delivery(), [Msg("msg-1", "worker_done")], PendingCount: 1));
        var tool = new AgentCheckTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("delivery").GetProperty("id").GetString().ShouldBe("del-1");
        doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString().ShouldBe("msg-1");
        doc.RootElement.GetProperty("pendingCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Dado_AckPassado_Quando_Check_Entao_RequestLevaAckId()
    {
        CheckDeliveriesRequest? captured = null;
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        service.CheckDeliveriesAsync(Arg.Any<CheckDeliveriesRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = (CheckDeliveriesRequest)ci[0];
                return new DeliveryCheckResult(null, []);
            });
        var tool = new AgentCheckTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"ack":"del-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        captured!.AckDeliveryId.ShouldBe("del-1");
    }

    [Fact]
    public async Task Dado_PeekEAll_Quando_Check_Entao_FlagsChegamAoService()
    {
        CheckDeliveriesRequest? captured = null;
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        service.CheckDeliveriesAsync(Arg.Any<CheckDeliveriesRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = (CheckDeliveriesRequest)ci[0];
                return new DeliveryCheckResult(null, [], Deliveries: [Delivery(acked: DateTime.UtcNow)]);
            });
        var tool = new AgentCheckTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("""{"all":true}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        captured!.All.ShouldBeTrue();
        result.Json.ShouldContain("del-1");
    }

    // ---- delegate_run_show ----

    [Fact]
    public async Task Dado_RunAberta_Quando_RunShow_Entao_ExpoeRunTasksDispatches()
    {
        var service = Substitute.For<IDelegationService>();
        service.GetRunShowAsync(null, "conv-1", Arg.Any<CancellationToken>())
            .Returns(new DelegationRunShowDto(
                new DelegationRunDto("run-1", "conv-1", "obj", "assistant",
                    DelegationRunStatus.Open, DateTime.UtcNow, null),
                [Task("task-1", activeDispatch: "disp-1")],
                [Dispatch("disp-1", attempt: 1)],
                [Delivery()]));
        var tool = new DelegateRunShowTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("run").GetProperty("id").GetString().ShouldBe("run-1");
        doc.RootElement.GetProperty("tasks")[0].GetProperty("activeDispatchId").GetString().ShouldBe("disp-1");
        doc.RootElement.GetProperty("dispatches")[0].GetProperty("attempt").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("unackedDeliveries")[0].GetProperty("id").GetString().ShouldBe("del-1");
    }

    [Fact]
    public async Task Dado_SemRun_Quando_RunShow_Entao_Refused()
    {
        var service = Substitute.For<IDelegationService>();
        service.GetRunShowAsync(null, "conv-1", Arg.Any<CancellationToken>())
            .Returns((DelegationRunShowDto?)null);
        var tool = new DelegateRunShowTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("no open run");
    }

    // ---- delegate_task retry receipt ----

    [Fact]
    public async Task Dado_RetryOf_Quando_DelegateTask_Entao_ReceiptComDispatchEAttempt()
    {
        var service = Substitute.For<IDelegationService>();
        service.RetryTaskAsync(
                "conv-1", "task-1", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RetryTaskResult(
                Task("task-1") with { Status = DelegationTaskStatus.Pending },
                Dispatch("disp-2", attempt: 2)));
        var git = Substitute.For<Taskboard.Integrations.Harness.IGitCommandRunner>();
        var tool = new DelegateTaskTool(TestScopeFactory.WithDelegation(service), git);

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"retry","retry_of":"task-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("taskId").GetString().ShouldBe("task-1");
        doc.RootElement.GetProperty("dispatchId").GetString().ShouldBe("disp-2");
        doc.RootElement.GetProperty("attempt").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Dado_RetryRecusado_Quando_DelegateTask_Entao_Refused()
    {
        var service = Substitute.For<IDelegationService>();
        service.RetryTaskAsync(
                "conv-1", "task-1", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<RetryTaskResult>(_ => throw new Taskboard.DomainException("X", "not retryable"));
        var git = Substitute.For<Taskboard.Integrations.Harness.IGitCommandRunner>();
        var tool = new DelegateTaskTool(TestScopeFactory.WithDelegation(service), git);

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"retry","retry_of":"task-1"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("not retryable");
    }

    // ---- P2: agent_ask / agent_reply / gates / task_update ----

    [Fact]
    public async Task Dado_Ask_Quando_Timeout_Entao_RetornaMessageIdTimedOut()
    {
        var service = Substitute.For<IDelegationService>();
        service.AskQuestionAsync(Arg.Any<AskQuestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PendingQuestionDto(
                "q-1", "conv-1", "run-1", "msg-1", "codex", "prosigo?", null,
                Taskboard.Delegation.PendingQuestionStatus.TimedOut, null,
                DateTime.UtcNow, DateTime.UtcNow));
        var tool = new AgentAskTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(
            Args("""{"question":"prosigo?","timeout_ms":500}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("messageId").GetString().ShouldBe("msg-1");
        doc.RootElement.GetProperty("status").GetString().ShouldBe("timedout");
    }

    [Fact]
    public async Task Dado_Reply_Quando_PerguntaPendente_Entao_Answered()
    {
        var service = Substitute.For<IDelegationService>();
        service.ReplyQuestionAsync("conv-1", "msg-1", "sim", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PendingQuestionDto(
                "q-1", "conv-1", "run-1", "msg-1", "codex", "prosigo?", null,
                Taskboard.Delegation.PendingQuestionStatus.Answered, "sim",
                DateTime.UtcNow, DateTime.UtcNow));
        var tool = new AgentReplyTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(
            Args("""{"message_id":"msg-1","answer":"sim"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("answered");
    }

    [Fact]
    public async Task Dado_GateCreate_Quando_TaskExiste_Entao_GateId()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateGateAsync("conv-1", "task-1", "aplicar?", Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationGateDto(
                "gate-1", "task-1", "conv-1", "aplicar?", null,
                Taskboard.Delegation.DelegationGateStatus.Open, null, DateTime.UtcNow, null));
        var tool = new DelegateGateCreateTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(
            Args("""{"task_id":"task-1","question":"aplicar?"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("gate-1");
    }

    [Fact]
    public async Task Dado_GateList_Quando_Scope_Entao_Lista()
    {
        var service = Substitute.For<IDelegationService>();
        service.ListGatesAsync("conv-1", null, Arg.Any<CancellationToken>())
            .Returns([new DelegationGateDto(
                "gate-1", "task-1", "conv-1", "aplicar?", null,
                Taskboard.Delegation.DelegationGateStatus.Open, null, DateTime.UtcNow, null)]);
        var tool = new DelegateGateListTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("gate-1");
    }

    [Fact]
    public async Task Dado_TaskUpdateBlocked_Quando_TaskExiste_Entao_Blocked()
    {
        var service = Substitute.For<IDelegationService>();
        service.BlockTaskAsync("conv-1", "task-1", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task("task-1") with { Status = DelegationTaskStatus.Blocked });
        var tool = new DelegateTaskUpdateTool(TestScopeFactory.WithDelegation(service));

        var result = await tool.ExecuteAsync(
            Args("""{"id":"task-1","status":"blocked","result":"{\"r\":\"quota\"}"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("blocked");
    }
}
