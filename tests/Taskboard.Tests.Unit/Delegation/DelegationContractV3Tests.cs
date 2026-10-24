using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261024-p1-agent-reliability Slice G (T7): delegation contract v3 —
/// <c>output_schema</c> annotates the completion with <c>schema_valid</c>
/// (never discards output), <c>role: orchestrator|leaf</c> makes the
/// delegation boundary contractual, <c>model</c> routes via the launch
/// resolver.
/// </summary>
public class DelegationContractV3Tests
{
    // ---------- entity ----------

    [Fact]
    public void Dado_SemRole_Quando_CriaTask_Entao_Leaf()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws");

        task.Role.ShouldBe(DelegationTaskRoles.Leaf);
    }

    [Fact]
    public void Dado_SemRole_Quando_CriaCoordinate_Entao_Orchestrator()
    {
        var task = DelegationTask.Create(
            "plan it", "codex", "conv-1", "/ws", kind: DelegationTaskKinds.Coordinate);

        task.Role.ShouldBe(DelegationTaskRoles.Orchestrator);
    }

    [Fact]
    public void Dado_CoordinateRoleLeaf_Quando_Cria_Entao_Recusa()
    {
        var ex = Should.Throw<DomainException>(() =>
            DelegationTask.Create(
                "plan it", "codex", "conv-1", "/ws",
                kind: DelegationTaskKinds.Coordinate, role: DelegationTaskRoles.Leaf));
        ex.Message.ShouldContain("orchestrator");
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("LEA F")]
    public void Dado_RoleInvalido_Quando_CriaTask_Entao_Recusa(string role)
    {
        var ex = Should.Throw<DomainException>(() =>
            DelegationTask.Create("p", "codex", "conv-1", "/ws", role: role));
        ex.Message.ShouldContain("role");
    }

    [Fact]
    public void Dado_RoleMaiusculo_Quando_CriaTask_Entao_Normaliza()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws", role: " Leaf ");

        task.Role.ShouldBe(DelegationTaskRoles.Leaf);
    }

    [Fact]
    public void Dado_RoleOrchestrator_Quando_CriaTask_Entao_Aceita()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws", role: "orchestrator");

        task.Role.ShouldBe(DelegationTaskRoles.Orchestrator);
    }

    [Fact]
    public void Dado_OutputSchemaInvalido_Quando_CriaTask_Entao_Recusa()
    {
        var ex = Should.Throw<DomainException>(() =>
            DelegationTask.Create("p", "codex", "conv-1", "/ws",
                outputSchema: "{not json"));
        ex.Message.ShouldContain("output_schema");
    }

    [Fact]
    public void Dado_OutputSchemaGigante_Quando_CriaTask_Entao_Recusa()
    {
        var huge = "{\"type\":\"string\",\"description\":\"" + new string('x', 9000) + "\"}";
        var ex = Should.Throw<DomainException>(() =>
            DelegationTask.Create("p", "codex", "conv-1", "/ws", outputSchema: huge));
        ex.Message.ShouldContain("output_schema");
    }

    [Fact]
    public void Dado_OutputSchemaValido_Quando_CriaTask_Entao_Persiste()
    {
        const string schema = """{"type":"object","required":["ok"]}""";
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws", outputSchema: schema);

        task.OutputSchema.ShouldBe(schema);
        task.SchemaValid.ShouldBeNull();
    }

    [Fact]
    public void Dado_SchemaValid_Quando_MarkDone_Entao_AnnotaSemDescartar()
    {
        var task = DelegationTask.Create(
            "p", "codex", "conv-1", "/ws",
            outputSchema: """{"type":"object"}""");

        task.MarkDone("full child output", schemaValid: false);

        task.Status.ShouldBe(DelegationTaskStatus.Done);
        task.ResultSummary.ShouldBe("full child output");
        task.SchemaValid.ShouldBe(false);
    }

    [Fact]
    public void Dado_TaskSchemaValidada_Quando_Reactivate_Entao_ResetaVeredicto()
    {
        var task = DelegationTask.Create(
            "p", "codex", "conv-1", "/ws", outputSchema: """{"type":"object"}""");
        task.MarkDone("out", schemaValid: true);

        task.MarkStale("lost");
        task.ReactivateForRetry();

        task.SchemaValid.ShouldBeNull();
    }

    // ---------- validator ----------

    private const string Schema = """{"type":"object","required":["summary"],"properties":{"summary":{"type":"string"}}}""";

    [Fact]
    public void Dado_OutputConforme_Quando_Valida_Entao_True()
    {
        OutputSchemaValidator.Evaluate(Schema, """{"summary":"done"}""").ShouldBeTrue();
    }

    [Fact]
    public void Dado_OutputNaoJson_Quando_Valida_Entao_False()
    {
        OutputSchemaValidator.Evaluate(Schema, "the work is done").ShouldBeFalse();
    }

    [Fact]
    public void Dado_OutputSemRequired_Quando_Valida_Entao_False()
    {
        OutputSchemaValidator.Evaluate(Schema, """{"other":1}""").ShouldBeFalse();
    }

    [Fact]
    public void Dado_OutputTipoErrado_Quando_Valida_Entao_False()
    {
        OutputSchemaValidator.Evaluate(Schema, """{"summary":42}""").ShouldBeFalse();
    }

    [Fact]
    public void Dado_OutputEmFence_Quando_Valida_Entao_True()
    {
        OutputSchemaValidator.Evaluate(
            Schema, "```json\n{\"summary\":\"done\"}\n```").ShouldBeTrue();
    }

    [Fact]
    public void Dado_ProsaMaisFenceJson_Quando_Valida_Entao_ExtraiEUltimo()
    {
        OutputSchemaValidator.Evaluate(
            Schema,
            "Done! Here is the result:\n```json\n{\"summary\":\"ok\"}\n```\nThanks.")
            .ShouldBeTrue();
    }

    [Fact]
    public void Dado_OutputNulo_Quando_Valida_Entao_False()
    {
        OutputSchemaValidator.Evaluate(Schema, null).ShouldBeFalse();
    }

    [Fact]
    public void Dado_SchemaInvalido_Quando_Valida_Entao_False()
    {
        OutputSchemaValidator.Evaluate("{not a schema", "{}").ShouldBeFalse();
    }

    // ---------- delegate_task tool ----------

    private static ChatToolContext Ctx(string? defaultCli = "codex") =>
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
            DefaultAgentCli: defaultCli);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static IServiceScopeFactory ScopeWith(IDelegationService service) =>
        new ServiceCollection().AddSingleton(service).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static DelegationTaskDto TaskDto(
        string id = "task-1", string? role = null, string? outputSchema = null) =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, DelegationTaskStatus.Pending, null, null,
            DateTime.UtcNow, null, null, null, Role: role, OutputSchema: outputSchema);

    [Fact]
    public async Task Dado_RoleESchema_Quando_DelegateTask_Entao_PassaNoRequest()
    {
        const string schema = """{"type":"object"}""";
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(role: "leaf", outputSchema: schema));
        var tool = new DelegateTaskTool(
            ScopeWith(service), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"report","role":"leaf","output_schema":"{\"type\":\"object\"}"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"role\":\"leaf\"");
        result.Json.ShouldContain("outputSchema");
        await service.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.Role == "leaf" && r.OutputSchema == schema),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RoleInvalido_Quando_DelegateTask_Entao_Recusa()
    {
        var service = Substitute.For<IDelegationService>();
        var tool = new DelegateTaskTool(
            ScopeWith(service), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"x","role":"boss"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("orchestrator|leaf");
        await service.DidNotReceive().CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_OutputSchemaNaoJson_Quando_DelegateTask_Entao_Recusa()
    {
        var service = Substitute.For<IDelegationService>();
        var tool = new DelegateTaskTool(
            ScopeWith(service), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"x","output_schema":"not json {"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("output_schema");
        await service.DidNotReceive().CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
    }

    // ---------- service: FinishRunAsync carries schema_valid ----------

    [Fact]
    public async Task Dado_SchemaValidFalse_Quando_FinishRun_Entao_FinalizeRecebeVeredicto()
    {
        var stubs = new DelegationServiceStubs();
        var done = DelegationServiceStubs.TaskDto("task-1", DelegationTaskStatus.Running);
        stubs.Tasks.FinalizeAsync(
                "task-1", DelegationTaskStatus.Done, Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(done);

        await stubs.Service.FinishRunAsync(
            "task-1", succeeded: true, "full output", schemaValid: false);

        await stubs.Tasks.Received(1).FinalizeAsync(
            "task-1", DelegationTaskStatus.Done, Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Is<bool?>(v => v == false), Arg.Any<CancellationToken>());
    }

    // ---------- dispatcher: leaf coordinator cannot fan out ----------

    [Fact]
    public async Task Dado_CoordinatorLeaf_Quando_Tick_Entao_FalhaSemPlanejar()
    {
        var delegation = Substitute.For<IDelegationService>();
        var tasks = Substitute.For<IDelegationTaskRepository>();
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var orchestration = Substitute.For<IAgentOrchestrationService>();
        var git = Substitute.For<IGitCommandRunner>();
        defs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var coordinator = new DelegationTaskDto(
            "task-c", "conv-1", "plan", "opencode", [], null, null, false, null,
            "/ws", null, null, DelegationTaskStatus.Ready, null, null,
            DateTime.UtcNow, null, null, null, Kind: DelegationTaskKinds.Coordinate,
            Role: DelegationTaskRoles.Leaf);
        delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([coordinator]);
        delegation.BeginRunAsync("task-c", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });
        tasks.GetAsync("task-c", Arg.Any<CancellationToken>())
            .Returns(coordinator with { Status = DelegationTaskStatus.Running });

        var dispatcher = new DelegationDispatcherService(
            new ServiceCollection()
                .AddSingleton(delegation).AddSingleton(tasks).AddSingleton(defs)
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            orchestration, git, new Taskboard.Integrations.Harness.Security.SecretScrubber(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DelegationDispatcherService>.Instance);

        await dispatcher.TickAsync();
        for (var i = 0; i < 100 && dispatcher.InflightCount > 0; i++)
        {
            await Task.Delay(50);
        }

        await orchestration.DidNotReceive().EnqueueAsync(
            Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>());
        await delegation.Received(1).FinishRunAsync(
            "task-c", false,
            Arg.Is<string>(s => s.Contains("orchestrator")),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }
}
