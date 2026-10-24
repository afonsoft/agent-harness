using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Chat.Hooks;
using Taskboard.Integrations.Harness.Security;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E02/E03 — emissão dos lifecycle hooks
/// no tool loop: tool.before antes do approval gate (block vira recusa
/// sintética sem perguntar), tool.after com o result head, run.start/end e a
/// chave Taskboard:Chat:Hooks:Enabled.
/// </summary>
public sealed class ChatServiceHookTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly ChatProvider _provider;
    private readonly List<HttpClient> _httpClients = [];
    private readonly ChatRunBroadcaster _broadcaster = new();
    private readonly ChatRunQueue _runQueue = new();
    private readonly ChatApprovalCoordinator _approvalCoordinator = new();
    private readonly List<IChatRunNotifier> _notifiers = [];
    private readonly List<TaskboardDbContext> _extraContexts = [];

    public ChatServiceHookTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-hooks-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();

        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();
    }

    private ChatService NewService(
        HttpMessageHandler handler,
        IEnumerable<IChatTool>? extraTools = null,
        IChatHookRegistry? hooks = null,
        Dictionary<string, string?>? extraConfig = null)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Taskboard:Chat:Tools:Enabled"] = "true",
            ["Taskboard:Chat:MaxToolIterations"] = "4",
        };
        if (extraConfig is not null)
        {
            foreach (var kv in extraConfig)
            {
                configValues[kv.Key] = kv.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        var tools = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["echo_tool"] = new FakeEchoTool(),
        };
        if (extraTools is not null)
        {
            foreach (var t in extraTools)
            {
                tools[t.Name] = t;
            }
        }

        return new ChatService(
            new EfCoreRepository<ChatProvider>(_context),
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatMessage>(_context),
            new OpenAiCompatibleClient(TrackHttp(handler)),
            new ChatCapabilityRegistry(tools, new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            new FakeWorkspaceResolver(),
            configuration,
            new ChatRunCoordinator(),
            NewHybridCache(),
            new EfCoreRepository<ChatRun>(_context),
            _runQueue,
            _broadcaster,
            new EfCoreRepository<ChatApproval>(_context),
            new EfCoreRepository<ChatSteer>(_context),
            _approvalCoordinator,
            _notifiers,
            NullLogger<ChatService>.Instance,
            hooks: hooks);
    }

    private static IChatHookRegistry Registry(
        IEnumerable<IChatHook> hooks, Dictionary<string, string?>? config = null) =>
        new ChatHookRegistry(hooks,
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
            NullLogger<ChatHookRegistry>.Instance);

    private async Task<List<ChatStreamEvent>> RunTurnAsync(
        ChatService service, string conversationId, string content)
    {
        var run = await service.EnqueueMessageAsync(conversationId, content);
        var runRow = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        runRow.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var runCts = new CancellationTokenSource();
        var events = new List<ChatStreamEvent>();
        await foreach (var chatEvent in service.ExecuteAsync(runRow, runCts, CancellationToken.None))
        {
            events.Add(chatEvent);
        }

        return events;
    }

    private static HybridCache NewHybridCache() =>
        new ServiceCollection().AddHybridCache().Services.BuildServiceProvider()
            .GetRequiredService<HybridCache>();

    private HttpClient TrackHttp(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        _httpClients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _httpClients)
        {
            client.Dispose();
        }

        foreach (var extra in _extraContexts)
        {
            extra.Dispose();
        }

        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private sealed class RecordingHook(
        string evt, string matcher = ".*",
        Func<HookContext, CancellationToken, Task<HookVerdict>>? run = null,
        string name = "recorder") : IChatHook
    {
        public List<HookContext> Seen { get; } = [];
        public string Name => name;
        public string Event => evt;
        public string Matcher => matcher;

        public Task<HookVerdict> RunAsync(HookContext context, CancellationToken cancellationToken)
        {
            Seen.Add(context);
            return run?.Invoke(context, cancellationToken) ?? Task.FromResult(HookVerdict.Allow);
        }
    }

    private sealed class FakeEchoTool : IChatTool
    {
        public int Executions { get; private set; }
        public string Name => "echo_tool";
        public string Description => "echo";
        public string ParametersJson => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { output = $"echo:{arguments.GetProperty("text").GetString()}" })));
        }
    }

    private sealed class FakeWriteTool : IChatTool
    {
        public string Name => "write_file";
        public string Description => "write";
        public string ParametersJson => """{"type":"object","properties":{"path":{"type":"string"}}}""";
        public bool RequiresConfirmation => true;
        public int Executions { get; private set; }

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { written = true })));
        }
    }

    /// <summary>Provider fake: 1ª chamada emite os tool calls dados; as demais respondem "done".</summary>
    private sealed class ToolCallsProviderHandler(string toolCallsJson) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var call = Interlocked.Increment(ref _calls);
            var body = call == 1
                ? Sse("""data: {"choices":[{"delta":{"tool_calls":[@CALLS@]}}]}"""
                    .Replace("@CALLS@", toolCallsJson, StringComparison.Ordinal))
                : Sse("""data: {"choices":[{"delta":{"content":"done"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";

        public static string Call(string id, string toolName, string argumentsJson, int index = 0)
        {
            var escaped = argumentsJson.Replace("\"", "\\\"", StringComparison.Ordinal);
            return $"{{\"index\":{index},\"id\":\"{id}\",\"function\":{{\"name\":\"{toolName}\",\"arguments\":\"{escaped}\"}}}}";
        }
    }

    private sealed class FakeWorkspaceResolver : IWorkspacePathResolver
    {
        public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
        {
            exists = false;
            return Path.GetTempPath();
        }

        public string? NormalizeWorkspacePath(string? path) => path;

        public WorkspaceDirsDto? ListSubdirs(string? path) => null;
    }

    private sealed class FakeSkillDiscovery : Taskboard.Application.Contracts.Skills.ISkillDiscoveryService
    {
        public Task<IReadOnlyList<Taskboard.Application.Contracts.Skills.SkillDto>> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.Application.Contracts.Skills.SkillDto>>([]);

        public Task<Taskboard.Application.Contracts.Skills.SkillDetailDto?> GetDetailAsync(
            string source, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Taskboard.Application.Contracts.Skills.SkillDetailDto?>(null);

        public Task<Taskboard.Application.Contracts.Skills.SkillFileResult> GetFileAsync(
            string source, string name, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Taskboard.Application.Contracts.Skills.SkillFileResult(
                Taskboard.Application.Contracts.Skills.SkillFileError.NotFound, null, null));
    }

    // ---- RF-E02/E03: tool.before ----

    [Fact]
    public async Task Dado_WriteEmSshPath_Quando_Turno_Entao_ConfigProtectionBloqueiaAntesDoGate()
    {
        var tool = new FakeWriteTool();
        var hooks = Registry([new ConfigProtectionHook(
            SensitivePathDenylist.Shared, Path.Join(Path.GetTempPath(), "harness-cfg-test"))]);
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "write_file", """{"path":".ssh/id_rsa"}"""));
        var service = NewService(handler, [tool], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, conversation.Id, "escreva");

        tool.Executions.ShouldBe(0, "o hook bloqueia antes da execução");
        _context.ChatApprovals.Count(a => a.ConversationId == ChatConversationId.From(conversation.Id))
            .ShouldBe(0, "o block do hook passa sem o approval gate");
        events.OfType<ChatApprovalAskedEvent>().ShouldBeEmpty();
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeTrue();
        result.ResultJson.ShouldContain("config-protection");
        result.ResultJson.ShouldContain("credential-path: ssh-dir");
    }

    [Fact]
    public async Task Dado_EngineDesabilitada_Quando_Turno_Entao_ToolExecuta()
    {
        var echo = new FakeEchoTool();
        var blocker = new RecordingHook(ChatHookEvents.ToolBefore, "echo_tool",
            run: (_, _) => Task.FromResult(HookVerdict.Block("não")), name: "blocker");
        var hooks = Registry([blocker], new() { [ChatHookRegistry.EnabledKey] = "false" });
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"rode"}"""));
        var service = NewService(handler, [echo], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, conversation.Id, "rode");

        blocker.Seen.ShouldBeEmpty("engine desabilitada não roda hooks");
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("echo:rode");
    }

    [Fact]
    public async Task Dado_HookWarn_Quando_ToolBefore_Entao_StatusNoteEToolExecuta()
    {
        var echo = new FakeEchoTool();
        var hooks = Registry([new RecordingHook(ChatHookEvents.ToolBefore, "echo_tool",
            run: (_, _) => Task.FromResult(HookVerdict.Warn("heads-up")), name: "watcher")]);
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"rode"}"""));
        var service = NewService(handler, [echo], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, conversation.Id, "rode");

        var note = events.OfType<ChatStatusEvent>().Single(e => e.Phase == "hook_note");
        note.Label!.ShouldContain("watcher");
        note.Label!.ShouldContain("heads-up");
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("echo:rode");
    }

    [Fact]
    public async Task Dado_ToolAfter_Quando_Executa_Entao_HookVeResultHead()
    {
        var recorder = new RecordingHook(ChatHookEvents.ToolAfter);
        var hooks = Registry([recorder]);
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"rode"}"""));
        var service = NewService(handler, [], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        await RunTurnAsync(service, conversation.Id, "rode");

        var ctx = recorder.Seen.ShouldHaveSingleItem();
        ctx.Subject.ShouldBe("echo_tool");
        ctx.RunId.ShouldNotBeNull();
        ctx.ResultHead!.ShouldContain("echo:rode");
    }

    // ---- RF-E02: run.start / run.end ----

    [Fact]
    public async Task Dado_RunStartEnd_Quando_Turno_Entao_EventosChegamNosHooks()
    {
        var start = new RecordingHook(ChatHookEvents.RunStart, name: "on-start");
        var end = new RecordingHook(ChatHookEvents.RunEnd, name: "on-end");
        var hooks = Registry([start, end]);
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"rode"}"""));
        var service = NewService(handler, [], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        await RunTurnAsync(service, conversation.Id, "rode");

        var startCtx = start.Seen.ShouldHaveSingleItem();
        startCtx.ConversationId.ShouldBe(conversation.Id);
        startCtx.RunId.ShouldNotBeNull();
        startCtx.Meta!.ShouldContainKey("provider");
        end.Seen.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dado_BlockNoRunStart_Quando_Turno_Entao_ErroSemChamarProvider()
    {
        var handler = new ToolCallsProviderHandler(
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"rode"}"""));
        var blocker = new RecordingHook(ChatHookEvents.RunStart,
            run: (_, _) => Task.FromResult(HookVerdict.Block("manutenção")), name: "kill-switch");
        var hooks = Registry([blocker]);
        var service = NewService(handler, [], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, conversation.Id, "rode");

        handler.Calls.ShouldBe(0, "run.start bloqueado nunca chega ao provider");
        events.OfType<ChatToolCallEvent>().ShouldBeEmpty();
        var done = events.OfType<ChatDoneEvent>().Single();
        done.FinishReason.ShouldBe("error");
        done.Error!.ShouldContain("kill-switch");
    }

    // ---- RF-E02: caminho paralelo (SPEC-20261023 P1) ----

    [Fact]
    public async Task Dado_BatchParalelo_Quando_HookBloqueia_Entao_TodosRecusadosSemExecutar()
    {
        var echo = new FakeEchoTool();
        var blocker = new RecordingHook(ChatHookEvents.ToolBefore, "echo_tool",
            run: (_, _) => Task.FromResult(HookVerdict.Block("negado")), name: "blocker");
        var hooks = Registry([blocker]);
        var handler = new ToolCallsProviderHandler(string.Join(",",
            ToolCallsProviderHandler.Call("call_1", "echo_tool", """{"text":"a"}""", index: 0),
            ToolCallsProviderHandler.Call("call_2", "echo_tool", """{"text":"b"}""", index: 1)));
        var service = NewService(handler, [echo], hooks);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, conversation.Id, "rode");

        echo.Executions.ShouldBe(0, "o batch inteiro é recusado pelo hook");
        blocker.Seen.Count.ShouldBe(2);
        events.OfType<ChatToolResultEvent>().Count().ShouldBe(2);
        events.OfType<ChatToolResultEvent>().ShouldAllBe(e => e.Refused);
    }
}
