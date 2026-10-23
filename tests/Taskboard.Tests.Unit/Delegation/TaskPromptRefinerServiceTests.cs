using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261022-task-prompt-refiner: service-level behavior — the
/// refined brief persisted at create (RF-004), retry re-refinement with
/// attempt context (RF-005/AC-02), per-task override, refined_prompt
/// diagnostics event (RF-008) and the automation fire path (AC-05).</summary>
public sealed class TaskPromptRefinerServiceTests
{
    private static IConfiguration Config(string mode) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [TaskPromptRefinementModes.ConfigurationKey] = mode,
            }).Build();

    private sealed class Fixture
    {
        public IDelegationTaskRepository Tasks { get; } =
            Substitute.For<IDelegationTaskRepository>();
        public IAgentMailboxRepository Mailbox { get; } =
            Substitute.For<IAgentMailboxRepository>();
        public IDelegationRunRepository Runs { get; } =
            Substitute.For<IDelegationRunRepository>();
        public IDispatchRepository Dispatches { get; } =
            Substitute.For<IDispatchRepository>();
        public IDeliveryRepository Deliveries { get; } =
            Substitute.For<IDeliveryRepository>();
        public IPendingQuestionRepository Questions { get; } =
            Substitute.For<IPendingQuestionRepository>();
        public IDelegationGateRepository Gates { get; } =
            Substitute.For<IDelegationGateRepository>();
        public DelegationService Service { get; }

        public Fixture(string mode)
        {
            var cfg = Config(mode);
            Service = new DelegationService(
                Tasks, Mailbox, Runs, Dispatches, Deliveries,
                Questions, Gates, cfg, new TaskPromptRefiner(cfg),
                TimeProvider.System);
        }
    }

    private static DelegationTaskDto TaskDto(
        string id = "task-1",
        DelegationTaskStatus status = DelegationTaskStatus.Pending,
        string prompt = "fix build",
        string? error = null,
        string? runId = "run-1") =>
        new(id, "conv-1", prompt, "codex", [], null, null, false, null,
            "/ws", "/repo", null, status, null, error,
            DateTime.UtcNow, null, null, null, RunId: runId);

    [Fact]
    public async Task Dado_CriarTask_Quando_TemplateMode_Entao_PersisteRefinadoEEmiteEvento()
    {
        // RF-004 + RF-008: refined persistido; evento refined_prompt no mailbox.
        var f = new Fixture(TaskPromptRefinementModes.Template);
        var service = f.Service;
        f.Runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>())
            .Returns((DelegationRunDto?)null);
        f.Runs.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationRunDto(
                "run-1", "conv-1", "obj", "assistant",
                DelegationRunStatus.Open, DateTime.UtcNow, null));
        CreateDelegationTaskRequest? captured = null;
        f.Tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = (CreateDelegationTaskRequest)ci[0];
                return TaskDto();
            });

        await service.CreateTaskAsync(
            new CreateDelegationTaskRequest(
                "fix build", "codex", "conv-1", "/ws", RepositoryPath: "/repo"),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.PromptRefined.ShouldNotBeNull();
        captured.PromptRefined.ShouldContain("## Task");
        captured.PromptRefined.ShouldContain("fix build");
        captured.PromptRefinement.ShouldBe(TaskPromptRefinementModes.Template);
        captured.Prompt.ShouldBe("fix build"); // raw preservado

        await f.Mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(m =>
                m.Kind == AgentMailboxKinds.RefinedPrompt
                && m.TaskId == "task-1"
                && m.Payload.Contains("\"mode\":\"template\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AutomationKind_Quando_CriarTask_Entao_Refinado()
    {
        // AC-05: o fire path da automação passa pelo mesmo seam → refinado.
        var f = new Fixture(TaskPromptRefinementModes.Template);
        var service = f.Service;
        f.Runs.GetOpenByScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationRunDto(
                "run-1", "automation:a1", "obj", "assistant",
                DelegationRunStatus.Open, DateTime.UtcNow, null));
        CreateDelegationTaskRequest? captured = null;
        f.Tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = (CreateDelegationTaskRequest)ci[0];
                return TaskDto();
            });

        await service.CreateTaskAsync(
            new CreateDelegationTaskRequest(
                "nightly digest", "codex", "automation:a1", "/ws",
                Kind: "automation"),
            CancellationToken.None);

        captured!.PromptRefined.ShouldNotBeNull();
        captured.PromptRefined.ShouldContain("nightly digest");
        captured.PromptRefined.ShouldContain("## Output contract");
    }

    [Fact]
    public async Task Dado_RetryDeTaskFalha_Quando_TemplateMode_Entao_ReRefinaComAttemptContext()
    {
        // AC-02/RF-005: retry aplica ApplyRefinement com o detalhe da falha.
        var f = new Fixture(TaskPromptRefinementModes.Template);
        var service = f.Service;
        f.Tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto(status: DelegationTaskStatus.Failed,
                error: "exit 1: build failed")]);
        f.Dispatches.LatestAttemptAsync("task-1", Arg.Any<CancellationToken>()).Returns(1);
        f.Tasks.ReactivateForRetryAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto(status: DelegationTaskStatus.Pending));
        f.Dispatches.AddAsync(Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (CreateDispatchRequest)ci[0];
                return new DispatchDto(
                    "disp-2", r.TaskId, r.RunId, "codex", r.Attempt,
                    DispatchState.Pending, "{}", null, null,
                    DateTime.UtcNow, null, null, null);
            });
        f.Tasks.ApplyRefinementAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => TaskDto(status: DelegationTaskStatus.Pending));

        var result = await service.RetryTaskAsync("conv-1", "task-1");

        result.Dispatch.Attempt.ShouldBe(2);
        await f.Tasks.Received(1).ApplyRefinementAsync(
            "task-1",
            Arg.Is<string>(s =>
                s.Contains("## Attempt context") && s.Contains("build failed")),
            TaskPromptRefinementModes.Template,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RetryComRefineOff_Quando_Retry_Entao_NaoReRefina()
    {
        // override off por task desliga a re-refinaria no retry.
        var f = new Fixture(TaskPromptRefinementModes.Template);
        var service = f.Service;
        f.Tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto(status: DelegationTaskStatus.Failed, error: "boom")]);
        f.Dispatches.LatestAttemptAsync("task-1", Arg.Any<CancellationToken>()).Returns(1);
        f.Tasks.ReactivateForRetryAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto(status: DelegationTaskStatus.Pending));
        f.Dispatches.AddAsync(Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (CreateDispatchRequest)ci[0];
                return new DispatchDto(
                    "disp-2", r.TaskId, r.RunId, "codex", r.Attempt,
                    DispatchState.Pending, "{}", null, null,
                    DateTime.UtcNow, null, null, null);
            });

        await service.RetryTaskAsync(
            "conv-1", "task-1", TaskPromptRefinementModes.Off);

        await f.Tasks.DidNotReceive().ApplyRefinementAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CriarComRefineOff_Quando_ConfigTemplate_Entao_PromptInalterado()
    {
        // AC-04: override off por task — PromptRefined nulo, raw preservado.
        var f = new Fixture(TaskPromptRefinementModes.Template);
        var service = f.Service;
        f.Runs.GetOpenByScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationRunDto(
                "run-1", "conv-1", "obj", "assistant",
                DelegationRunStatus.Open, DateTime.UtcNow, null));
        CreateDelegationTaskRequest? captured = null;
        f.Tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = (CreateDelegationTaskRequest)ci[0];
                return TaskDto();
            });

        await service.CreateTaskAsync(
            new CreateDelegationTaskRequest(
                "fix build", "codex", "conv-1", "/ws",
                Refine: TaskPromptRefinementModes.Off),
            CancellationToken.None);

        captured!.PromptRefined.ShouldBeNull();
        await f.Mailbox.DidNotReceive().AddAsync(
            Arg.Is<PostMailboxMessageRequest>(m =>
                m.Kind == AgentMailboxKinds.RefinedPrompt),
            Arg.Any<CancellationToken>());
    }
}
