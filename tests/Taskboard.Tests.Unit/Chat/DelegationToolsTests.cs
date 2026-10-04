using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-chat-agent-delegation: run_agent enfileira runs correlacionados
/// à conversa; task roda sub-agente read-only sem recursão.
/// </summary>
public class DelegationToolsTests
{
    private static ChatToolContext Ctx(
        IReadOnlyDictionary<string, IChatTool>? toolSet = null,
        int depth = 0,
        RecordingActivity? activity = null,
        string? defaultAgentCli = null,
        string? defaultAgentModel = null) =>
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
            Activity: activity,
            ToolSet: toolSet,
            DefaultAgentCli: defaultAgentCli,
            DefaultAgentModel: defaultAgentModel);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ---- run_agent ----

    [Fact]
    public async Task Dado_RunAgent_Quando_WaitFalse_Entao_EnfileiraERetornaRunId()
    {
        var orchestration = new FakeOrchestration(eligible: true);
        var tool = new RunAgentTool(orchestration, Config());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"faça X","wait":false}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("queued");
        orchestration.LastRequest!.IssueId.ShouldStartWith("chat:conv-1:");
        orchestration.LastRequest.Instructions.ShouldContain("delegated from chat conversation conv-1");
    }

    [Fact]
    public async Task Dado_DuasDelegacoes_Quando_MesmaConversa_Entao_IssueIdsDistintos()
    {
        // B-07: cada delegação gera um id único — wait=true não pode observar
        // o run de uma delegação anterior da mesma conversa.
        var orchestration = new FakeOrchestration(eligible: true);
        var tool = new RunAgentTool(orchestration, Config());
        var ctx = Ctx();

        await tool.ExecuteAsync(Args("""{"prompt":"primeira","wait":false}"""), ctx, CancellationToken.None);
        await tool.ExecuteAsync(Args("""{"prompt":"segunda","wait":false}"""), ctx, CancellationToken.None);

        orchestration.Requests.Count.ShouldBe(2);
        orchestration.Requests[0].IssueId.ShouldNotBe(orchestration.Requests[1].IssueId);
        orchestration.Requests.ShouldAllBe(r => r.IssueId.StartsWith("chat:conv-1:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_SemCliElegivel_Entao_Refused()
    {
        var orchestration = new FakeOrchestration(eligible: false);
        var tool = new RunAgentTool(orchestration, Config());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"faça X"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("no eligible agent");
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_EmSubAgent_Entao_RecursaoBloqueada()
    {
        var orchestration = new FakeOrchestration(eligible: true);
        var tool = new RunAgentTool(orchestration, Config());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"x"}"""), Ctx(depth: 1), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        orchestration.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_Enfileira_Entao_ReportaRunningAgent()
    {
        var orchestration = new FakeOrchestration(eligible: true);
        var activity = new RecordingActivity();
        var tool = new RunAgentTool(orchestration, Config());

        await tool.ExecuteAsync(Args("""{"prompt":"x","wait":false}"""), Ctx(activity: activity), CancellationToken.None);

        activity.Phases.ShouldContain("running_agent");
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_SemArgECliVinculado_Entao_UsaCliDoContexto()
    {
        // SPEC-20261003-ai-code-agent-chat: o CLI da barra Agent é o default
        // quando a tool call omite "agent".
        var orchestration = new FakeOrchestration(eligible: true, agents:
        [
            new AgentInfo("codex", "/bin/codex", AgentType.Codex, AgentStatus.Available, "1.0", null),
            new AgentInfo("devin", "/bin/devin", AgentType.Devin, AgentStatus.Available, "1.0", null),
        ]);
        var tool = new RunAgentTool(orchestration, Config());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"faça X","wait":false}"""),
            Ctx(defaultAgentCli: "Devin"), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        orchestration.LastRequest!.AgentType.ShouldBe(AgentType.Devin);
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_ModeloVinculado_Entao_PropagaResolvedModelName()
    {
        var orchestration = new FakeOrchestration(eligible: true);
        var tool = new RunAgentTool(orchestration, Config());

        await tool.ExecuteAsync(
            Args("""{"prompt":"x","wait":false}"""),
            Ctx(defaultAgentModel: "gpt-codex-x"), CancellationToken.None);

        orchestration.LastRequest!.ResolvedModelName.ShouldBe("gpt-codex-x");
        orchestration.LastRequest.OmitModelFlag.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_RunAgent_Quando_SemModeloVinculado_Entao_OmiteFlagDeModelo()
    {
        var orchestration = new FakeOrchestration(eligible: true);
        var tool = new RunAgentTool(orchestration, Config());

        await tool.ExecuteAsync(
            Args("""{"prompt":"x","wait":false}"""), Ctx(), CancellationToken.None);

        orchestration.LastRequest!.ResolvedModelName.ShouldBeNull();
        orchestration.LastRequest.OmitModelFlag.ShouldBeTrue();
    }

    // ---- task (sub-agent) ----

    [Fact]
    public async Task Dado_SubAgent_Quando_ChamaTask_Entao_Refused_RecursaoBloqueada()
    {
        using var http = new HttpClient(new PlainHandler());
        var tool = new SubAgentTool(new OpenAiCompatibleClient(http));

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"explore"}"""), Ctx(depth: 1), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldBe("recursion blocked");
    }

    [Fact]
    public async Task Dado_Task_Quando_Executa_Entao_RetornaRespostaInline()
    {
        using var http = new HttpClient(new PlainHandler());
        var tool = new SubAgentTool(new OpenAiCompatibleClient(http));

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"resuma o workspace"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("resposta do sub");
        result.Json.ShouldContain("\"iterations\":1");
    }

    [Fact]
    public async Task Dado_Task_Quando_Executa_Entao_ToolSetSemWriteNemRecursao()
    {
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured);
        var tool = new SubAgentTool(new OpenAiCompatibleClient(http));
        var toolSet = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["read_file"] = new StubTool("read_file"),
            ["write_file"] = new StubTool("write_file"),
            ["task"] = new StubTool("task"),
            ["run_agent"] = new StubTool("run_agent"),
        };

        await tool.ExecuteAsync(
            Args("""{"prompt":"explore"}"""), Ctx(toolSet: toolSet), CancellationToken.None);

        var body = captured.LastBody!;
        body.ShouldContain("read_file");
        body.ShouldNotContain("write_file");
        body.ShouldNotContain("\"task\"");
        body.ShouldNotContain("run_agent");
    }

    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build();

    private sealed class RecordingActivity : IChatActivityReporter
    {
        public List<string> Phases { get; } = [];
        public void Report(string phase, string label) => Phases.Add(phase);
    }

    private sealed class FakeOrchestration(bool eligible, IReadOnlyList<AgentInfo>? agents = null)
        : IAgentOrchestrationService
    {
        public List<AgentExecutionRequest> Requests { get; } = [];

        public AgentExecutionRequest? LastRequest => Requests.LastOrDefault();

        public Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(agents ?? (IReadOnlyList<AgentInfo>)(eligible
                ? [new AgentInfo("codex", "/bin/codex", AgentType.Codex, AgentStatus.Available, "1.0", null)]
                : []));

        public Task<bool> EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentLogMessage>>([]);

        public Task ClearLogsAsync(string issueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> CancelAsync(string issueId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<AgentRunDto>> GetRunsAsync(string issueId, int take = 5, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentRunDto>>([]);

        public Task<IReadOnlyList<AgentRunDto>> GetLatestRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentRunDto>>([]);

        public IReadOnlyCollection<Guid> GetLiveRunIds() => [];
    }

    /// <summary>Provider fake: resposta final direta (sem tool calls).</summary>
    private sealed class PlainHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"choices":[{"delta":{"content":"resposta do sub"},"finish_reason":"stop"}]}

                    data: [DONE]

                    """, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    /// <summary>Captura o payload para inspecionar o toolset enviado ao provider.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"choices":[{"delta":{"content":"ok"},"finish_reason":"stop"}]}

                    data: [DONE]

                    """, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private sealed class StubTool(string name) : IChatTool
    {
        public string Name => name;
        public string Description => name;
        public string ParametersJson => """{"type":"object"}""";
        public Task<ChatToolResult> ExecuteAsync(JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult("{}"));
    }
}
