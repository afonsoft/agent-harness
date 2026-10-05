using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20260929-ai-code-provider-chat RF-004/RF-005: ciclo completo do tool
/// loop contra um provider fake — user → assistant(tool_calls) → tool →
/// assistant(final), com persistência e eventos na ordem.
/// </summary>
public sealed class ChatServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly ChatService _service;
    private readonly ChatProvider _provider;
    private readonly List<HttpClient> _httpClients = [];
    private readonly ChatRunBroadcaster _broadcaster = new();
    private readonly ChatRunQueue _runQueue = new();
    private readonly ChatRunCoordinator _coordinator = new();
    private readonly ChatApprovalCoordinator _approvalCoordinator = new();
    private readonly List<IChatRunNotifier> _notifiers = [];
    private readonly List<TaskboardDbContext> _extraContexts = [];

    public ChatServiceTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-chat-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();

        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();

        _service = NewService(new FakeProviderHandler(), _coordinator);
    }

    private ChatService NewService(HttpMessageHandler handler, ChatRunCoordinator coordinator, IChatTool? extraTool = null, IWorkspacePathResolver? workspace = null, TaskboardDbContext? context = null, Dictionary<string, string?>? extraConfig = null, IEnumerable<IChatTool>? extraTools = null)
    {
        context ??= _context;
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
        if (extraTool is not null)
        {
            tools[extraTool.Name] = extraTool;
        }
        if (extraTools is not null)
        {
            foreach (var t in extraTools)
            {
                tools[t.Name] = t;
            }
        }
        return new ChatService(
            new EfCoreRepository<ChatProvider>(context),
            new EfCoreRepository<ChatConversation>(context),
            new EfCoreRepository<ChatMessage>(context),
            new OpenAiCompatibleClient(TrackHttp(handler)),
            new ChatCapabilityRegistry(tools, new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            workspace ?? new FakeWorkspaceResolver(),
            configuration,
            coordinator,
            NewHybridCache(),
            new EfCoreRepository<ChatRun>(context),
            _runQueue,
            _broadcaster,
            new EfCoreRepository<ChatApproval>(context),
            _approvalCoordinator,
            _notifiers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatService>.Instance);
    }

    /// <summary>DbContext separado no mesmo SQLite — o "outro scope" do /stop.</summary>
    private TaskboardDbContext NewSecondContext()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        var ctx = new TaskboardDbContext(options);
        _extraContexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// SPEC-20261005: o dispatcher em miniatura — enqueue (persiste user + run
    /// Queued) e depois ExecuteAsync com o runCts do coordinator, igual ao
    /// ChatRunDispatcherService.RunItemAsync.
    /// </summary>
    private async Task<List<ChatStreamEvent>> RunTurnAsync(
        ChatService service, ChatRunCoordinator coordinator, string conversationId, string content)
    {
        var run = await service.EnqueueMessageAsync(conversationId, content);
        return await ExecuteRunAsync(service, coordinator, conversationId, run.Id);
    }

    private async Task<List<ChatStreamEvent>> ExecuteRunAsync(
        ChatService service, ChatRunCoordinator coordinator, string conversationId, string runId)
    {
        var run = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(runId));
        run.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var runCts = await coordinator.BeginAsync(conversationId);
        try
        {
            var events = new List<ChatStreamEvent>();
            await foreach (var chatEvent in service.ExecuteAsync(run, runCts, CancellationToken.None))
            {
                events.Add(chatEvent);
            }

            return events;
        }
        finally
        {
            coordinator.End(conversationId, runCts);
            runCts.Dispose();
        }
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

    [Fact]
    public async Task Dado_ProviderFake_Quando_EnviarComToolCall_Entao_LoopCompletoComEventosOrdenados()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(_service, _coordinator, conversation.Id, "rode ls");

        // 1º turno: tool call + tool result; 2º: resposta final.
        events.OfType<ChatToolCallEvent>().Select(e => e.Name).ShouldBe(["echo_tool"]);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("echo:rode");
        events.OfType<ChatDeltaEvent>().Select(e => e.Content).ShouldBe(["pronto"]);
        events.OfType<ChatPersistedEvent>().ShouldNotBeEmpty(
            "SPEC-20261005 RF-003: o marcador de persistência separa o parcial do durável");
        events.OfType<ChatDoneEvent>().ShouldHaveSingleItem().FinishReason.ShouldBe("stop");

        // RF-004: transcript persistido na ordem user → assistant(tool) → tool → assistant.
        var detail = await _service.GetConversationAsync(conversation.Id);
        detail.ShouldNotBeNull();
        detail.Messages.Select(m => m.Role).ShouldBe(["user", "assistant", "tool", "assistant"]);
        detail.Messages[1].ToolCallsJson.ShouldNotBeNull();
        detail.Messages[2].ToolName.ShouldBe("echo_tool");
        detail.Messages[3].Content.ShouldBe("pronto");
        detail.Conversation.Title.Contains("rode", StringComparison.Ordinal).ShouldBeTrue("título derivado da primeira mensagem (RF-004)");
    }

    [Fact]
    public async Task Dado_NomeDuplicado_Quando_CriarProvider_Entao_ChatValidationException()
    {
        await _service.CreateProviderAsync(new ChatProviderUpsertRequest("dup", "http://x.test", "k"));

        var exception = await Should.ThrowAsync<ChatValidationException>(
            () => _service.CreateProviderAsync(new ChatProviderUpsertRequest("dup", "http://other.test", "k")));

        exception.Message.Contains("already exists", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_BuscaPorConteudo_Quando_ListarConversas_Entao_ConversaEncontrada()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await RunTurnAsync(_service, _coordinator, conversation.Id, "termo-unico-xyz");

        var list = await _service.ListConversationsAsync("termo-unico-xyz");

        list.ShouldContain(c => c.Id == conversation.Id);
    }

    [Fact]
    public async Task Dado_WorkspacePathForaDeHome_Quando_CriarEPatchear_Entao_ClampaParaNullOuResolve()
    {
        // SPEC-20261004 RF-002: WorkspacePath é normalizado contra $HOME —
        // caminho fora vira null (o run volta ao default ~/repos).
        var home = Directory.CreateTempSubdirectory("tb-home-");
        try
        {
            var service = NewService(
                new FakeProviderHandler(), new ChatRunCoordinator(),
                workspace: new Taskboard.Integrations.Workspace.WorkspaceService(
                    null, home.FullName,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Taskboard.Integrations.Workspace.WorkspaceService>.Instance));

            var conversation = await service.CreateConversationAsync(
                new CreateChatConversationRequest(_provider.Id, "m1",
                    Agent: new ChatAgentContext(AgentCli: "Devin", WorkspacePath: "/etc")));
            conversation.Agent.ShouldNotBeNull();
            conversation.Agent.AgentCli.ShouldBe("Devin");
            conversation.Agent.WorkspacePath.ShouldBeNull();

            var patched = await service.PatchConversationAsync(
                conversation.Id,
                new PatchChatConversationRequest(Agent: new ChatAgentContext(WorkspacePath: "proj")));
            patched!.Agent!.WorkspacePath.ShouldBe(Path.Combine(home.FullName, "repos", "proj"));
        }
        finally
        {
            Directory.Delete(home.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Dado_ConversaComAgentContext_Quando_CriarEPatchear_Entao_Persiste()
    {
        // SPEC-20261003-ai-code-agent-chat: o vínculo agente (CLI/repo/modelo)
        // persiste na conversa e PATCH substitui o bloco inteiro.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1",
                Agent: new ChatAgentContext(
                    AgentCli: "Devin", RepositoryFullName: "owner/repo", AgentModel: "devin-x")));

        conversation.Agent.ShouldNotBeNull();
        conversation.Agent.AgentCli.ShouldBe("Devin");
        conversation.Agent.RepositoryFullName.ShouldBe("owner/repo");
        conversation.Agent.AgentModel.ShouldBe("devin-x");

        var reopened = await _service.GetConversationAsync(conversation.Id);
        reopened.ShouldNotBeNull();
        reopened.Conversation.Agent!.AgentCli.ShouldBe("Devin");

        var patched = await _service.PatchConversationAsync(
            conversation.Id,
            new PatchChatConversationRequest(Agent: new ChatAgentContext(AgentCli: "Codex")));
        patched.ShouldNotBeNull();
        patched.Agent!.AgentCli.ShouldBe("Codex");
        patched.Agent.RepositoryFullName.ShouldBeNull("PATCH substitui o contexto inteiro");

        var cleared = await _service.PatchConversationAsync(
            conversation.Id,
            new PatchChatConversationRequest(Agent: new ChatAgentContext()));
        cleared.ShouldNotBeNull();
        cleared.Agent.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ProviderInexistente_Quando_Enviar_Entao_ChatValidationException()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await _service.DeleteProviderAsync(_provider.Id);

        await Should.ThrowAsync<ChatValidationException>(
            async () => await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None));
    }

    [Fact]
    public async Task Dado_ConversaArquivada_Quando_Enqueue_Entao_ChatArchivedException()
    {
        // RF-006: arquivada é view flag — não aceita novo turno até restaurar.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var archived = await _service.SetConversationArchivedAsync(conversation.Id, archived: true);
        archived.ShouldNotBeNull().ArchivedAt.ShouldNotBeNull();

        await Should.ThrowAsync<ChatArchivedException>(
            async () => await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None));

        var restored = await _service.SetConversationArchivedAsync(conversation.Id, archived: false);
        restored.ShouldNotBeNull().ArchivedAt.ShouldBeNull();
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        run.Status.ShouldBe("queued");
    }

    [Fact]
    public async Task Dado_RunQueued_Quando_StopAsync_Entao_QueuedViraStopped()
    {
        // RF-004: stop cancela também runs na fila — nada recomeça depois.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);

        (await _service.StopAsync(conversation.Id)).ShouldBeTrue();

        var row = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        row.Status.ShouldBe(ChatRunStatus.Stopped);
        row.Error.ShouldBe("stopped by user");
    }

    [Fact]
    public async Task Dado_SegundaMensagemNaFila_Quando_ExecutaRun1_Entao_TranscriptNaoCarregaUserDepois()
    {
        // RF-002 FIFO: user rows persisted AFTER the run's trigger belong to
        // later queued turns — they must not leak into this run's wire.
        var handler = new RecordingProviderHandler();
        var service = NewService(handler, _coordinator);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run1 = await service.EnqueueMessageAsync(conversation.Id, "primeira", CancellationToken.None);
        var run2 = await service.EnqueueMessageAsync(conversation.Id, "segunda-enquanto-roda", CancellationToken.None);

        await ExecuteRunAsync(service, _coordinator, conversation.Id, run1.Id);

        var wire1 = handler.RequestBodies[0];
        wire1.ShouldContain("primeira");
        wire1.ShouldNotContain("segunda-enquanto-roda");

        await ExecuteRunAsync(service, _coordinator, conversation.Id, run2.Id);
        handler.RequestBodies[1].ShouldContain("segunda-enquanto-roda");
    }

    [Fact]
    public async Task Dado_Detail_Quando_RunQueued_Entao_ActiveRunEListBadge()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);

        var detail = await _service.GetConversationAsync(conversation.Id);
        detail.ShouldNotBeNull();
        detail.ActiveRun.ShouldNotBeNull().Id.ShouldBe(run.Id);
        detail.LastRun.ShouldBeNull("nenhum run terminal ainda");

        var list = await _service.ListConversationsAsync(query: null);
        list.Single(c => c.Id == conversation.Id).ActiveRunStatus.ShouldBe("queued");
    }

    [Fact]
    public async Task Dado_RunConcluido_Quando_GetRunSnapshot_Entao_SyncComMensagensETerminal()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        var entity = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        entity.Start(DateTime.UtcNow);
        entity.Complete(5, 3, DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var sync = await _service.GetRunSnapshotAsync(conversation.Id, run.Id, CancellationToken.None);

        sync.ShouldNotBeNull();
        sync.Run.Status.ShouldBe("completed");
        sync.Run.TokensIn.ShouldBe(5);
        sync.Messages.Select(m => m.Role).ShouldBe(["user"]);
        sync.Partial.ShouldBeNull("run terminal não carrega checkpoint");
    }

    /// <summary>Provider fake que grava o corpo do request — transcript introspection.</summary>
    private sealed class RecordingProviderHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            const string body = """data: {"choices":[{"delta":{"content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""" + "\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    [Fact]
    public async Task Dado_RunRegistradoPorOutraInstancia_Quando_Stop_Entao_CoordinatorCancelaStream()
    {
        // B-01: o ChatService é scoped — /stop chega em outra instância. Com o
        // coordinator singleton compartilhado, o stop alcança o run ativo.
        var coordinator = new ChatRunCoordinator();
        var handler = new GatedProviderHandler();
        var sender = NewService(handler, coordinator);
        // O stopper é o "outro request scope": mesmo coordinator singleton,
        // DbContext próprio (EF não é thread-safe).
        var stopper = NewService(handler, coordinator, context: NewSecondContext());

        var conversation = await sender.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var collect = Task.Run(() => RunTurnAsync(sender, coordinator, conversation.Id, "oi"));

        await handler.FirstDeltaSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await stopper.StopAsync(conversation.Id)).ShouldBeTrue(
            "B-01: o stop de outro request scope alcança o run via coordinator singleton");

        var events = await collect.WaitAsync(TimeSpan.FromSeconds(10));
        events.OfType<ChatDeltaEvent>().Select(e => e.Content).ShouldBe(["parte-1"]);
        events.OfType<ChatDoneEvent>().Single().Error.ShouldBe("stopped by user");
    }

    [Fact]
    public async Task Dado_ProviderAindaTransmitindo_Quando_ConsomeStream_Entao_DeltaChegaAntesDoFim()
    {
        // B-02: streaming real — o 1º delta deve sair do enumerador enquanto o
        // provider ainda está bloqueado no gate; com buffering ele só chegaria
        // depois do stream completo.
        var handler = new GatedProviderHandler();
        var coordinator = new ChatRunCoordinator();
        var service = NewService(handler, coordinator);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var run = await service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        var entity = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        entity.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();
        var runCts = await coordinator.BeginAsync(conversation.Id);
        var stream = service.ExecuteAsync(entity, runCts, CancellationToken.None);
        await using var enumerator = stream.GetAsyncEnumerator();

        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<ChatDeltaEvent>().Content.ShouldBe("parte-1");
        handler.ReleaseSecond.TrySetResult();

        var rest = new List<ChatStreamEvent>();
        while (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
        {
            rest.Add(enumerator.Current);
        }

        rest.OfType<ChatDeltaEvent>().Select(e => e.Content).ShouldBe(["parte-2"]);
        rest.OfType<ChatDoneEvent>().ShouldHaveSingleItem();
        coordinator.End(conversation.Id, runCts);
        runCts.Dispose();
    }

    [Fact]
    public async Task Dado_StreamComReasoning_Quando_Enviar_Entao_ReasoningEventNaoPersiste()
    {
        var service = NewService(new ReasoningProviderHandler(), new ChatRunCoordinator());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "deepseek-r1"));

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        events.OfType<ChatReasoningEvent>().Select(e => e.Content).ShouldBe(["thinking", "pong"]);
        var detail = await service.GetConversationAsync(conversation.Id);
        var assistant = detail!.Messages.Last();
        assistant.Role.ShouldBe("assistant");
        assistant.Content.ShouldBe("pong", "reasoning não vira conteúdo persistido");
    }

    /// <summary>Provider fake que emite reasoning_content antes da resposta.</summary>
    private sealed class ReasoningProviderHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var body = string.Concat(
                """data: {"choices":[{"delta":{"content":"","reasoning_content":"thinking"}}]}""", "\n",
                """data: {"choices":[{"delta":{"reasoning_content":"pong"}}]}""", "\n",
                """data: {"choices":[{"delta":{"content":"pong"}}],"usage":{"prompt_tokens":5,"completion_tokens":3}}""", "\n",
                "data: [DONE]\n");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    /// <summary>B-17: tool result with imagePath attaches the image to the tool message.</summary>
    [Fact]
    public async Task Dado_ToolRetornaImagePath_Quando_Enviar_Entao_MensagemToolComImagem()
    {
        var service = NewService(new FakeImageProviderHandler(), new ChatRunCoordinator(), new FakeImageTool());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "gere um gato");

        var detail = await service.GetConversationAsync(conversation.Id);
        var toolMessage = detail!.Messages.Single(m => m.Role == "tool");
        toolMessage.ImagePath.ShouldBe("imgs/cat.png");
        toolMessage.ImageUrl.ShouldBe("/api/local/chat/images/imgs/cat.png");
    }

    private sealed class FakeImageTool : IChatTool
    {
        public string Name => "generate_image";
        public string Description => "image";
        public string ParametersJson => """{"type":"object"}""";

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { imagePath = "imgs/cat.png", prompt = "cat" })));
    }

    /// <summary>Provider fake: 1ª chamada dispara generate_image, 2ª encerra.</summary>
    private sealed class FakeImageProviderHandler : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var call = Interlocked.Increment(ref _calls);
            var body = call == 1
                ? """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_img","function":{"name":"generate_image","arguments":"{\"prompt\":\"cat\"}"}}]}}]}""" + "\ndata: [DONE]\n"
                : """data: {"choices":[{"delta":{"content":"aqui está"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""" + "\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    /// <summary>
    /// SPEC-20261001-ai-chat-openwebui RF-001: modelos que emitem tool call como
    /// markup inline (DeepSeek <｜DSML｜function_calls>) — o markup nunca vira
    /// delta nem persiste; a chamada é materializada e executa no tool loop.
    /// </summary>
    [Fact]
    public async Task Dado_ProviderEmiteDsml_Quando_Enviar_Entao_MarkupNaoVazaEChamadaExecuta()
    {
        var service = NewService(new DsmlProviderHandler(), new ChatRunCoordinator());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "deepseek"));

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "rode ls");

        events.OfType<ChatToolCallEvent>().Select(e => e.Name).ShouldBe(["echo_tool"]);
        events.OfType<ChatDeltaEvent>().Select(e => e.Content).ShouldBe(["vou rodar ", "feito"]);
        events.OfType<ChatDeltaEvent>()
            .ShouldNotContain(d => d.Content.Contains("DSML", StringComparison.Ordinal));

        var detail = await service.GetConversationAsync(conversation.Id);
        detail!.Messages.ShouldNotContain(m => m.Content.Contains("DSML", StringComparison.Ordinal));
        detail.Messages[1].ToolCallsJson.ShouldNotBeNull("o call materializado do markup persiste como tool call real");
        detail.Messages[2].ToolName.ShouldBe("echo_tool");
    }

    /// <summary>Provider fake: 1ª chamada emite DSML inline, 2ª devolve a resposta final.</summary>
    private sealed class DsmlProviderHandler : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var call = Interlocked.Increment(ref _calls);
            var body = call == 1
                ? Sse("""data: {"choices":[{"delta":{"content":"vou rodar <｜DSML｜function_calls><｜DSML｜invoke name=\"echo_tool\"><｜DSML｜parameter name=\"text\" string=\"true\">rode</｜DSML｜parameter></｜DSML｜invoke></｜DSML｜function_calls>"}}]}""")
                : Sse("""data: {"choices":[{"delta":{"content":"feito"}}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";
    }

    private sealed class FakeEchoTool : IChatTool
    {
        public string Name => "echo_tool";
        public string Description => "echo";
        public string ParametersJson => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { output = $"echo:{arguments.GetProperty("text").GetString()}" })));
    }

    // ---- SPEC-20261005-chat-tool-approval ----

    /// <summary>Tool mutante fake — RequiresConfirmation + nome no MutatingTools.</summary>
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

    /// <summary>Provider fake: chamadas ímpares emitem write_file, pares respondem "done".</summary>
    private sealed class MutatingProviderHandler : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var call = Interlocked.Increment(ref _calls);
            var body = call % 2 == 1
                ? Sse("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_w","function":{"name":"write_file","arguments":"{\"path\":\"/tmp/x\"}"}}]}}]}""")
                : Sse("""data: {"choices":[{"delta":{"content":"done"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";
    }

    /// <summary>Espera a linha ChatApproval pending aparecer (o gate suspendeu a tool).</summary>
    private async Task<ChatApproval> WaitForPendingApprovalAsync(string conversationId)
    {
        for (var i = 0; i < 400; i++)
        {
            var pending = await _context.ChatApprovals
                .Where(a => a.ConversationId == ChatConversationId.From(conversationId))
                .ToListAsync();
            if (pending.FirstOrDefault(a => a.Status == ChatApprovalStatus.Pending) is { } found)
            {
                return found;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("nenhuma ChatApproval pending em 10s");
    }

    private ChatService NewApprovalService(
        Dictionary<string, string?>? extraConfig = null,
        FakeWriteTool? tool = null,
        ChatRunCoordinator? coordinator = null) =>
        NewService(new MutatingProviderHandler(), coordinator ?? new ChatRunCoordinator(),
            extraTool: tool ?? new FakeWriteTool(), extraConfig: extraConfig);

    // ---- Plan mode (SPEC-20261005-chat-plan-mode) ----

    /// <summary>Tool set do plan mode: exit_plan_mode + write_file (mutante) + echo_tool.</summary>
    private ChatService NewPlanService(
        HttpMessageHandler handler,
        ChatRunCoordinator? coordinator = null,
        FakeWriteTool? tool = null,
        Dictionary<string, string?>? extraConfig = null) =>
        NewService(handler, coordinator ?? new ChatRunCoordinator(),
            extraTool: tool ?? new FakeWriteTool(),
            extraTools: [new Taskboard.Integrations.Chat.Tools.ExitPlanModeTool()],
            extraConfig: extraConfig);

    [Fact]
    public async Task Dado_PlanModeOn_Quando_ToolMutanteChamada_Entao_RecusaSemApproval()
    {
        var tool = new FakeWriteTool();
        var service = NewPlanService(new MutatingProviderHandler(), tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        (await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None))
            .ShouldNotBeNull();

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(0, "tool mutante não executa em plan mode");
        _context.ChatApprovals.Count(a => a.ConversationId == ChatConversationId.From(conversation.Id))
            .ShouldBe(0, "a recusa de plan mode não cria approval");
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeTrue();
        result.ResultJson.ShouldContain("Plan mode is active");
    }

    [Fact]
    public async Task Dado_ExitPlanMode_Quando_PlanoSemTituloMarkdown_Entao_ErroSemReview()
    {
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", """{"plan":"plano sem titulo"}"""));
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");

        _context.ChatApprovals.Count(a => a.ConversationId == ChatConversationId.From(conversation.Id))
            .ShouldBe(0, "plano inválido não cria review");
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.ResultJson.ShouldContain("must be markdown starting with");
        result.Refused.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ExitPlanMode_Quando_PlanoExcedeLimite_Entao_ErroSemReview()
    {
        // Sem \n: escaping SSE→JSON viraria newline literal e inválida o inner JSON antes do check de tamanho.
        var oversizedPlan = $"{{\"plan\":\"# T{new string('x', ChatApproval.PlanPreviewMaxLength)}\"}}";
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", oversizedPlan));
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");

        _context.ChatApprovals.Count(a => a.ConversationId == ChatConversationId.From(conversation.Id))
            .ShouldBe(0);
        events.OfType<ChatToolResultEvent>().Single().ResultJson
            .ShouldContain("exceeds");
    }

    [Fact]
    public async Task Dado_ReviewRejeitada_Quando_Feedback_Entao_ContinuaEmPlanMode()
    {
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", """{"plan":"# Plano\\n- passo 1"}"""));
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);
        approval.Kind.ShouldBe(ChatApprovalKind.PlanReview);
        approval.ArgumentsPreview.ShouldContain("# Plano");

        var decided = await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("deny", "faltou detalhe"), CancellationToken.None);
        decided!.Status.ShouldBe("rejected");

        var events = await runTask;
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeFalse("rejeição de plano não é refusal — o modelo revisa");
        result.ResultJson.ShouldContain("approved");
        result.ResultJson.ShouldContain("faltou detalhe");

        var detail = await service.GetConversationAsync(conversation.Id, CancellationToken.None);
        detail!.Conversation.PlanMode.ShouldBe("on", "plano rejeitado mantém plan mode");
    }

    [Fact]
    public async Task Dado_ReviewAprovada_Quando_ExitPlanMode_Entao_SaiDePlanModeComNotas()
    {
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", """{"plan":"# Plano\\n- passo 1"}"""));
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);

        await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("allow"), CancellationToken.None);

        var events = await runTask;
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.ResultJson.ShouldContain("\"approved\":true");

        var detail = await service.GetConversationAsync(conversation.Id, CancellationToken.None);
        detail!.Conversation.PlanMode.ShouldBe("off");
        detail.Messages.ShouldContain(m => m.Role == "system"
            && m.Content.Contains("plan mode off", StringComparison.Ordinal));
        detail.Messages.ShouldContain(m => m.Role == "system"
            && m.Content.Contains("Plan review allowed-once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dado_ReviewPendente_Quando_DesativaPlanMode_Entao_CancelaReview()
    {
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", """{"plan":"# Plano"}"""));
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);

        var updated = await service.SetPlanModeAsync(conversation.Id, false, CancellationToken.None);
        updated!.PlanMode.ShouldBe("off");

        var events = await runTask;
        var stored = await _context.ChatApprovals.AsNoTracking()
            .SingleAsync(a => a.Id == approval.Id);
        stored.Status.ShouldBe(ChatApprovalStatus.Cancelled);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("approved");
    }

    [Fact]
    public async Task Dado_ReviewExpirada_Quando_Timeout_Entao_ContinuaEmPlanModeFailClosed()
    {
        var service = NewPlanService(
            new PlanReviewProviderHandler("exit_plan_mode", """{"plan":"# Plano"}"""),
            extraConfig: new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Approval:TimeoutSeconds"] = "1",
            });
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "planeje");

        var result = events.OfType<ChatToolResultEvent>().Single();
        result.ResultJson.ShouldContain("timed out");
        var detail = await service.GetConversationAsync(conversation.Id, CancellationToken.None);
        detail!.Conversation.PlanMode.ShouldBe("on", "review expirada é fail-closed");
    }

    [Fact]
    public async Task Dado_PlanModeOn_Quando_TurnoNormal_Entao_PromptContemSecaoPlano()
    {
        var handler = new PlanReviewProviderHandler("echo_tool", """{"text":"oi"}""");
        var service = NewPlanService(handler);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "leiame");

        handler.Bodies.ShouldNotBeEmpty();
        handler.Bodies[0].ShouldContain("Plan mode is active");
    }


    [Fact]
    public async Task Dado_PresetAsk_Quando_ToolMutanteAprovada_Entao_ExecutaEGeraAuditoria()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        conversation.PermissionPreset.ShouldBe("ask", "preset default global é ask (RF-006)");

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);
        approval.ToolName.ShouldBe("write_file");
        approval.Status.ShouldBe(ChatApprovalStatus.Pending);

        var decided = await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("allow"), CancellationToken.None);
        decided.ShouldNotBeNull();
        decided.Status.ShouldBe("allowed-once");

        var events = await runTask;
        tool.Executions.ShouldBe(1);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("written");

        // RNF-003: decisão auditável como system note no transcript.
        var detail = await service.GetConversationAsync(conversation.Id);
        detail!.Messages.ShouldContain(m => m.Role == "system"
            && m.Content.Contains("allowed-once", StringComparison.Ordinal));
        detail.PendingApprovals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_PresetAsk_Quando_NegarComMotivo_Entao_ToolResultRefused()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);

        var decided = await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("deny", "not allowed"), CancellationToken.None);
        decided!.Status.ShouldBe("rejected");

        var events = await runTask;
        tool.Executions.ShouldBe(0);
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeTrue();
        result.ResultJson.ShouldContain("denied by the user");
        result.ResultJson.ShouldContain("not allowed");

        var row = await _context.ChatApprovals.SingleAsync(a => a.Id == approval.Id);
        row.DecidedBy.ShouldBe(ChatApprovalDecidedBy.Ui);
    }

    [Fact]
    public async Task Dado_PresetChat_Quando_ToolMutante_Entao_RecusaPreExecucaoSemApproval()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "chat"), CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(0);
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeTrue();
        result.ResultJson.ShouldContain("permission preset");
        result.RefusalReason.ShouldBe("denied by permission policy");
        _context.ChatApprovals.ShouldBeEmpty("preset chat recusa sem criar approval — não há o que decidir (RF-006)");
    }

    [Fact]
    public async Task Dado_PresetFull_Quando_ToolMutante_Entao_ExecutaDireto()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "full"), CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(1);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("written");
        _context.ChatApprovals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ToolPolicyNever_Quando_PresetFull_Entao_RecusaMesmoAssim()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(
            extraConfig: new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Approval:ToolPolicy:write_file"] = "never",
            },
            tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "full"), CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(0);
        events.OfType<ChatToolResultEvent>().Single().Refused.ShouldBeTrue(
            "per-tool never vence o preset full (RF-008 ordem §3)");
    }

    [Fact]
    public async Task Dado_RememberTool_Quando_Aprovar_Entao_ProximaChamadaNaoPede()
    {
        var tool = new FakeWriteTool();
        var coordinator = new ChatRunCoordinator();
        var service = NewApprovalService(tool: tool, coordinator: coordinator);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var runTask = RunTurnAsync(service, coordinator, conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);
        await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("allow", RememberTool: true), CancellationToken.None);
        await runTask;

        tool.Executions.ShouldBe(1);
        var entity = await _context.ChatConversations.SingleAsync(c => c.Id == ChatConversationId.From(conversation.Id));
        entity.AllowedTools().ShouldContain("write_file");

        // Segunda chamada à mesma tool: sem approval — allowed-list resolve.
        var events = await RunTurnAsync(service, coordinator, conversation.Id, "escreva de novo");
        tool.Executions.ShouldBe(2);
        _context.ChatApprovals.Count().ShouldBe(1, "a segunda chamada não gerou approval novo");
    }

    [Fact]
    public async Task Dado_ApprovalJaDecidida_Quando_DecidirDeNovo_Entao_409()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);

        await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("allow"), CancellationToken.None);

        await Should.ThrowAsync<ChatApprovalConflictException>(
            () => service.DecideApprovalAsync(
                approval.Id.Value, new DecideChatApprovalRequest("deny"), CancellationToken.None));

        await runTask;
    }

    [Fact]
    public async Task Dado_TimeoutEsgotado_Quando_NinguemDecide_Entao_UnavailableEDenied()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(
            extraConfig: new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Approval:TimeoutSeconds"] = "1",
            },
            tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(0);
        var result = events.OfType<ChatToolResultEvent>().Single();
        result.Refused.ShouldBeTrue();
        result.ResultJson.ShouldContain("timed out");
        result.RefusalReason.ShouldBe("approval unavailable");

        var row = await _context.ChatApprovals.SingleAsync();
        row.Status.ShouldBe(ChatApprovalStatus.Unavailable);
        row.DecidedBy.ShouldBe(ChatApprovalDecidedBy.AutoTimeout);
    }

    [Fact]
    public async Task Dado_GateDesabilitado_Quando_ToolMutante_Entao_ExecutaSemPedir()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(
            extraConfig: new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Approval:Enabled"] = "false",
            },
            tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(1);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("written");
        _context.ChatApprovals.ShouldBeEmpty("RNF-004: Enabled=false = zero gate");
    }

    [Fact]
    public async Task Dado_StopComApprovalPendente_Quando_Parar_Entao_ApprovalCancelada()
    {
        var tool = new FakeWriteTool();
        var coordinator = new ChatRunCoordinator();
        var service = NewApprovalService(tool: tool, coordinator: coordinator);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var runTask = RunTurnAsync(service, coordinator, conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);

        // Stop vem de OUTRO scope (outro DbContext) — igual ao endpoint real.
        var stopService = NewService(
            new MutatingProviderHandler(), coordinator,
            extraTool: new FakeWriteTool(), context: NewSecondContext());
        (await stopService.StopAsync(conversation.Id, CancellationToken.None)).ShouldBeTrue();

        var events = await runTask.WaitAsync(TimeSpan.FromSeconds(10));
        events.OfType<ChatDoneEvent>().Single().Error.ShouldBe("stopped by user");

        var row = await _context.ChatApprovals.SingleAsync(a => a.Id == approval.Id);
        row.Status.ShouldBe(ChatApprovalStatus.Cancelled);
        row.DecidedBy.ShouldBe(ChatApprovalDecidedBy.AutoCancel);
        tool.Executions.ShouldBe(0);
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

    // SPEC-20261004-provider-pick-hybridcache RF-002: catálogo em HybridCache —
    // lista de providers e modelos por provider com invalidação nos writes.

    [Fact]
    public async Task Dado_ModelosJaListados_Quando_ListarDeNovo_Entao_ServidoDoCacheSemHttp()
    {
        var handler = new ModelsHandler();
        var service = NewService(handler, new ChatRunCoordinator());

        var first = await service.ListModelsAsync(_provider.Id);
        var second = await service.ListModelsAsync(_provider.Id);

        first.Models.ShouldBe(["m1", "m2"]);
        second.Models.ShouldBe(first.Models);
        handler.Calls.ShouldBe(1, "a segunda chamada devia sair do cache");
        second.Cached.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_InsertDiretoNoContext_Quando_ListarProviders_Entao_ServeCacheInvalidadoPorWrite()
    {
        var first = await _service.ListProvidersAsync();
        first.Count.ShouldBe(1);

        // Escrita por fora do service não invalida — o cache segue servindo.
        _context.ChatProviders.Add(ChatProvider.Create("sneaky", "http://other.test", "sk"));
        _context.SaveChanges();
        (await _service.ListProvidersAsync()).Count.ShouldBe(1);

        // Write via service invalida o catálogo.
        await _service.UpdateProviderAsync(_provider.Id, new ChatProviderUpsertRequest("renamed", "http://provider.test"));
        var fresh = await _service.ListProvidersAsync();
        fresh.Select(p => p.Name).ShouldBe(["renamed", "sneaky"]);
    }

    [Fact]
    public async Task Dado_ProviderDeletado_Quando_ListarModels_Entao_CacheDoModeloInvalidado()
    {
        var handler = new ModelsHandler();
        var service = NewService(handler, new ChatRunCoordinator());
        await service.ListModelsAsync(_provider.Id);

        // Update de qualquer provider derruba a tag de modelos.
        await service.UpdateProviderAsync(_provider.Id, new ChatProviderUpsertRequest("fake", "http://provider.test"));
        await service.ListModelsAsync(_provider.Id);

        handler.Calls.ShouldBe(2, "update do provider devia invalidar o cache de modelos");
    }

    private sealed class ModelsHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"id":"m1"},{"id":"m2"}]}""",
                    Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Provider fake: 1ª chamada devolve tool_calls, 2ª devolve a resposta final.</summary>
    private sealed class FakeProviderHandler : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var call = Interlocked.Increment(ref _calls);
            var body = call == 1
                ? Sse("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"echo_tool","arguments":"{\"text\":\"rode\"}"}}]}}]}""")
                : Sse("""data: {"choices":[{"delta":{"content":"pronto"}}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";
    }

    /// <summary>
    /// Provider fake: 1ª chamada emite o tool call configurado (exit_plan_mode,
    /// echo_tool, ...) e registra os bodies; as demais respondem "done".
    /// </summary>
    private sealed class PlanReviewProviderHandler(string toolName, string argumentsJson) : HttpMessageHandler
    {
        private int _calls;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var call = Interlocked.Increment(ref _calls);
            var escapedArgs = argumentsJson.Replace("\"", "\\\"", StringComparison.Ordinal);
            var body = call == 1
                ? Sse("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_plan","function":{"name":"@NAME@","arguments":"@ARGS@"}}]}}]}"""
                    .Replace("@NAME@", toolName, StringComparison.Ordinal)
                    .Replace("@ARGS@", escapedArgs, StringComparison.Ordinal))
                : Sse("""data: {"choices":[{"delta":{"content":"done"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";
    }

    /// <summary>
    /// Provider fake controlado: emite o 1º delta, sinaliza o teste e espera
    /// <see cref="ReleaseSecond"/> (ou cancelamento) antes de enviar o resto.
    /// </summary>
    private sealed class GatedProviderHandler : HttpMessageHandler
    {
        public TaskCompletionSource FirstDeltaSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new GatedStream(FirstDeltaSent, ReleaseSecond.Task)),
            };
        }

        private sealed class GatedStream(TaskCompletionSource firstSent, Task releaseSecond) : Stream
        {
            private static readonly byte[] First = Encoding.UTF8.GetBytes(
                """data: {"choices":[{"delta":{"content":"parte-1"}}]}""" + "\n\n");

            private static readonly byte[] Second = Encoding.UTF8.GetBytes(
                """data: {"choices":[{"delta":{"content":"parte-2"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""" + "\n\ndata: [DONE]\n");

            private readonly Queue<byte[]> _pending = new();
            private int _offset;
            private int _phase;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                while (_pending.Count == 0)
                {
                    if (_phase == 0)
                    {
                        _pending.Enqueue(First);
                        _phase = 1;
                        firstSent.TrySetResult();
                        continue;
                    }

                    if (_phase == 2)
                    {
                        return 0;
                    }

                    _phase = 2;
                    await releaseSecond.WaitAsync(cancellationToken);
                    _pending.Enqueue(Second);
                }

                var chunk = _pending.Peek();
                var n = Math.Min(buffer.Length, chunk.Length - _offset);
                chunk.AsMemory(_offset, n).CopyTo(buffer);
                _offset += n;
                if (_offset == chunk.Length)
                {
                    _pending.Dequeue();
                    _offset = 0;
                }

                return n;
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }
}
