using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261009 RF-001: plan extractor, shared parser, delegate_coordinate
/// tool and the dispatcher's coordinate-task path.
/// </summary>
public class DelegationCoordinatorTests
{
    // ---------- CoordinatorPlanExtractor ----------

    [Fact]
    public void Dado_SaidaComPlanoJson_Quando_TryExtract_Entao_RetornaTasks()
    {
        var output = "Here is my plan:\n{\"tasks\":[{\"prompt\":\"a\"},{\"prompt\":\"b\",\"deps\":[0]}]}\nDone.";

        CoordinatorPlanExtractor.TryExtract(output, out var tasks).ShouldBeTrue();
        tasks.GetArrayLength().ShouldBe(2);
        tasks[0].GetProperty("prompt").GetString().ShouldBe("a");
    }

    [Fact]
    public void Dado_DoisObjetosComTasks_Quando_TryExtract_Entao_UsaOUltimo()
    {
        var output = "{\"tasks\":[{\"prompt\":\"first\"}]} text {\"tasks\":[{\"prompt\":\"last\"}]}";

        CoordinatorPlanExtractor.TryExtract(output, out var tasks).ShouldBeTrue();
        tasks[0].GetProperty("prompt").GetString().ShouldBe("last");
    }

    [Fact]
    public void Dado_PlanoDentroDeFence_Quando_TryExtract_Entao_Extrai()
    {
        var output = "```json\n{\"tasks\":[{\"prompt\":\"x\"}]}\n```";

        CoordinatorPlanExtractor.TryExtract(output, out var tasks).ShouldBeTrue();
        tasks.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void Dado_ChavesDentroDeString_Quando_TryExtract_Entao_NaoConfunde()
    {
        var output = "note \"{not json}\" then {\"tasks\":[{\"prompt\":\"has } brace\"}]}";

        CoordinatorPlanExtractor.TryExtract(output, out var tasks).ShouldBeTrue();
        tasks[0].GetProperty("prompt").GetString().ShouldBe("has } brace");
    }

    [Fact]
    public void Dado_ObjetoSemTasksDepoisPlano_Quando_TryExtract_Entao_IgnoraSemTasks()
    {
        var output = "{\"tasks\":[{\"prompt\":\"p\"}]} {\"other\":1}";

        CoordinatorPlanExtractor.TryExtract(output, out var tasks).ShouldBeTrue();
        tasks[0].GetProperty("prompt").GetString().ShouldBe("p");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json at all")]
    [InlineData("{\"tasks\":\"not-array\"}")]
    [InlineData("{\"tasks\":[{\"prompt\":\"x\"}]")]
    public void Dado_SaidaSemPlano_Quando_TryExtract_Entao_Falso(string output)
    {
        CoordinatorPlanExtractor.TryExtract(output, out _).ShouldBeFalse();
    }

    // ---------- DelegationPlanParser ----------

    private static JsonElement TasksEl(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Dado_DepParaTaskFutura_Quando_ParseTasks_Entao_Erro()
    {
        var specs = DelegationPlanParser.ParseTasks(
            TasksEl("[{\"prompt\":\"a\",\"deps\":[1]},{\"prompt\":\"b\"}]"), 10, out var error);

        specs.ShouldBeNull();
        error!.ShouldContain("earlier tasks");
    }

    [Fact]
    public void Dado_DepNegativo_Quando_ParseTasks_Entao_Erro()
    {
        var specs = DelegationPlanParser.ParseTasks(
            TasksEl("[{\"prompt\":\"a\"},{\"prompt\":\"b\",\"deps\":[-1]}]"), 10, out var error);

        specs.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_PlanoValido_Quando_ParseTasks_Entao_SpecsComDeps()
    {
        var specs = DelegationPlanParser.ParseTasks(
            TasksEl("[{\"prompt\":\"a\",\"cli\":\"codex\"},{\"prompt\":\"b\",\"deps\":[0],\"use_worktree\":true}]"),
            10, out var error);

        error.ShouldBeNull();
        specs!.Count.ShouldBe(2);
        specs[1].Deps.ShouldBe([0]);
        specs[1].UseWorktree.ShouldBeTrue();
        specs[0].Cli.ShouldBe("codex");
    }

    // ---------- DelegateCoordinatorTool ----------

    private static ChatToolContext Ctx(string? defaultCli = "codex", int depth = 0) =>
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
            DelegationDepth: depth,
            DefaultAgentCli: defaultCli);

    private static IServiceScopeFactory ScopeWith(IDelegationService service)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(service);
        return collection.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static DelegationTaskDto TaskDto(string id = "coord-1") =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, DelegationTaskStatus.Pending, null, null,
            DateTime.UtcNow, null, null, null, DelegationTaskKinds.Coordinate);

    [Fact]
    public async Task Dado_Goal_Quando_DelegateCoordinate_Entao_CriaTaskCoordinate()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto());
        var tool = new DelegateCoordinatorTool(
            ScopeWith(service), Substitute.For<IGitCommandRunner>());

        tool.RequiresConfirmation.ShouldBeTrue();
        var result = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>("{\"goal\":\"ship feature\"}"), Ctx(), default);

        result.Refused.ShouldBeFalse();
        await service.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.Kind == DelegationTaskKinds.Coordinate && r.CliName == "codex"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_MaxTasks_Quando_DelegateCoordinate_Entao_LimitaNoPrompt()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto());
        var tool = new DelegateCoordinatorTool(
            ScopeWith(service), Substitute.For<IGitCommandRunner>());

        await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>("{\"goal\":\"g\",\"max_tasks\":3}"), Ctx(), default);

        await service.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r => r.Prompt.Contains("at most 3")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DepthPositivo_Quando_DelegateCoordinate_Entao_Recusa()
    {
        var tool = new DelegateCoordinatorTool(
            ScopeWith(Substitute.For<IDelegationService>()), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>("{\"goal\":\"g\"}"), Ctx(depth: 1), default);

        result.Refused.ShouldBeTrue();
    }

    // ---------- dispatcher coordinate path ----------

    [Fact]
    public async Task Dado_CoordinatorTask_Quando_PlannerEmitePlano_Entao_MaterializaFilhos()
    {
        var delegation = Substitute.For<IDelegationService>();
        var tasks = Substitute.For<IDelegationTaskRepository>();
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var orchestration = Substitute.For<IAgentOrchestrationService>();
        var git = Substitute.For<IGitCommandRunner>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var coordinator = TaskDto() with
        {
            CliName = "Codex",
            Kind = DelegationTaskKinds.Coordinate,
            Status = DelegationTaskStatus.Ready,
        };
        delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([coordinator]);
        delegation.BeginRunAsync("coord-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });
        tasks.GetAsync("coord-1", Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });
        orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        orchestration.GetRunsAsync("task:coord-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:coord-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        orchestration.GetLogsAsync("task:coord-1", Arg.Any<CancellationToken>())
            .Returns([
                new AgentLogMessage(
                    DateTimeOffset.UtcNow, "task:coord-1", AgentLogStream.StdOut,
                    "plan: {\"tasks\":[{\"prompt\":\"child one\"},{\"prompt\":\"child two\",\"deps\":[0]}]}"),
            ]);

        var childIndex = 0;
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TaskDto($"child-{++childIndex}") with { Kind = DelegationTaskKinds.Task });

        var collection = new ServiceCollection();
        collection.AddSingleton(delegation);
        collection.AddSingleton(tasks);
        collection.AddSingleton(defs);
        var provider = collection.BuildServiceProvider();
        collection.AddSingleton(new DelegationPlanCreator(
            provider.GetRequiredService<IServiceScopeFactory>()));
        var scopeFactory = collection.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var dispatcher = new DelegationDispatcherService(
            scopeFactory, orchestration, git, new SecretScrubber(),
            new ConfigurationBuilder().Build(),
            NullLogger<DelegationDispatcherService>.Instance);

        await dispatcher.TickAsync();
        for (var i = 0; i < 200 && dispatcher.InflightCount > 0; i++)
        {
            await Task.Delay(20);
        }

        // Two child tasks materialized; second waits on the first.
        await delegation.Received(2).CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.DependsOn != null && r.DependsOn.Count == 1),
            Arg.Any<CancellationToken>());
        await delegation.Received(1).FinishRunAsync(
            "coord-1", true,
            Arg.Is<string?>(s => s!.Contains("plan materialized")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_PlannerSemPlano_Quando_CoordinatorRoda_Entao_FalhaComMotivo()
    {
        var delegation = Substitute.For<IDelegationService>();
        var tasks = Substitute.For<IDelegationTaskRepository>();
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var orchestration = Substitute.For<IAgentOrchestrationService>();
        var git = Substitute.For<IGitCommandRunner>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var coordinator = TaskDto() with
        {
            CliName = "Codex",
            Kind = DelegationTaskKinds.Coordinate,
            Status = DelegationTaskStatus.Ready,
        };
        delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([coordinator]);
        delegation.BeginRunAsync("coord-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });
        tasks.GetAsync("coord-1", Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });
        orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        orchestration.GetRunsAsync("task:coord-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:coord-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        orchestration.GetLogsAsync("task:coord-1", Arg.Any<CancellationToken>())
            .Returns([
                new AgentLogMessage(
                    DateTimeOffset.UtcNow, "task:coord-1", AgentLogStream.StdOut, "no plan here"),
            ]);

        var collection = new ServiceCollection();
        collection.AddSingleton(delegation);
        collection.AddSingleton(tasks);
        collection.AddSingleton(defs);
        var provider = collection.BuildServiceProvider();
        collection.AddSingleton(new DelegationPlanCreator(
            provider.GetRequiredService<IServiceScopeFactory>()));
        var scopeFactory = collection.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var dispatcher = new DelegationDispatcherService(
            scopeFactory, orchestration, git, new SecretScrubber(),
            new ConfigurationBuilder().Build(),
            NullLogger<DelegationDispatcherService>.Instance);

        await dispatcher.TickAsync();
        for (var i = 0; i < 200 && dispatcher.InflightCount > 0; i++)
        {
            await Task.Delay(20);
        }

        await delegation.DidNotReceive().CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
        await delegation.Received(1).FinishRunAsync(
            "coord-1", false,
            Arg.Is<string?>(s => s!.Contains("plan")), Arg.Any<CancellationToken>());
    }
}
