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

    private ChatService NewService(HttpMessageHandler handler, ChatRunCoordinator coordinator, IChatTool? extraTool = null, IWorkspacePathResolver? workspace = null, TaskboardDbContext? context = null, Dictionary<string, string?>? extraConfig = null, IEnumerable<IChatTool>? extraTools = null, ISpillStore? spillStore = null)
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
            new EfCoreRepository<ChatSteer>(context),
            _approvalCoordinator,
            _notifiers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatService>.Instance,
            spillStore: spillStore);
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

    // ---- SPEC-20261012-chat-run-controls: pause/resume ----

    [Fact]
    public async Task Dado_RunQueued_Quando_PauseRun_Entao_PausedEPersistido()
    {
        // RF-003: run ainda na fila vira paused na hora — sem executor para sinalizar.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);

        var dto = await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);

        dto.Status.ShouldBe("paused");
        dto.PausedAt.ShouldNotBeNull();
        var row = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        row.Status.ShouldBe(ChatRunStatus.Paused);

        var detail = await _service.GetConversationAsync(conversation.Id);
        detail.ShouldNotBeNull().ActiveRun.ShouldNotBeNull("paused segue ativa — RF-001 IsActive");
    }

    [Fact]
    public async Task Dado_RunPausedSemExecutor_Quando_ResumeRun_Entao_RequeueParaFila()
    {
        // RF-003: paused sem executor vivo re-entra na fila — a dispatcher
        // re-dirige o turno a partir da transcript.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);

        var dto = await _service.ResumeRunAsync(conversation.Id, run.Id, CancellationToken.None);

        dto.Status.ShouldBe("queued");
        dto.PausedAt.ShouldBeNull();

        // A fila carrega o enqueue original + o requeue do resume.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = _runQueue.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        (await reader.MoveNextAsync()).ShouldBeTrue();
        reader.Current.RunId.ShouldBe(run.Id);
        (await reader.MoveNextAsync()).ShouldBeTrue("resume re-enfileira o work item");
        reader.Current.RunId.ShouldBe(run.Id);
    }

    [Fact]
    public async Task Dado_RunPaused_Quando_Stop_Entao_Stopped()
    {
        // RF-005: stop alcança a run pausada sem executor.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);

        (await _service.StopAsync(conversation.Id)).ShouldBeTrue();

        var row = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        row.Status.ShouldBe(ChatRunStatus.Stopped);
        row.PausedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunTerminal_Quando_PauseOuResume_Entao_ChatConflictException()
    {
        // RF-003/RF-004: pause e resume rejeitam run terminal com 409 no endpoint.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        var entity = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        entity.Start(DateTime.UtcNow);
        entity.Complete(5, 3, DateTime.UtcNow);
        await _context.SaveChangesAsync();

        await Should.ThrowAsync<ChatConflictException>(
            () => _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None));
        await Should.ThrowAsync<ChatConflictException>(
            () => _service.ResumeRunAsync(conversation.Id, run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Dado_RunPausedOuRunning_Quando_RepetePauseResume_Entao_Idempotente()
    {
        // Pause em paused e resume em running são no-ops — o cliente pode
        // chamar de novo sem erro (botão clicado duas vezes).
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);
        await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);

        var again = await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);
        again.Status.ShouldBe("paused");

        var entity = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        entity.Resume();
        await _context.SaveChangesAsync();
        var resume = await _service.ResumeRunAsync(conversation.Id, run.Id, CancellationToken.None);
        resume.Status.ShouldBe("queued");
    }

    [Fact]
    public async Task Dado_RunSnapshot_Quando_GetRunSnapshot_Entao_LastActivityEStallThreshold()
    {
        // RF-007: attach entrega o stamp de atividade + o threshold usado
        // na derivação de stalled no cliente.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "oi", CancellationToken.None);

        var sync = await _service.GetRunSnapshotAsync(conversation.Id, run.Id, CancellationToken.None);

        sync.ShouldNotBeNull();
        sync.StallThresholdSeconds.ShouldBe(120);
        sync.LastActivityUtc.ShouldNotBeNull("sem eventos ainda, cai no StartedAt/criação");
    }

    [Fact]
    public async Task Dado_FlagDePausa_Quando_Executa_Entao_ParkNoBoundaryEResumeCompleta()
    {
        // RF-002: pause flag estaciona o executor no boundary — chat.paused,
        // row paused, espera; resume acorda → chat.resumed → turno completa.
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await _service.EnqueueMessageAsync(conversation.Id, "rode ls", CancellationToken.None);
        var entity = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
        entity.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var runCts = await _coordinator.BeginAsync(conversation.Id);
        try
        {
            // PauseRunAsync com executor vivo vira flag — o park acontece no stream.
            var flagged = await _service.PauseRunAsync(conversation.Id, run.Id, CancellationToken.None);
            flagged.Status.ShouldBe("running", "executor vivo só consome o flag no boundary");

            var events = new List<ChatStreamEvent>();
            await using var enumerator = _service
                .ExecuteAsync(entity, runCts, CancellationToken.None)
                .GetAsyncEnumerator();
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            enumerator.Current.ShouldBeOfType<ChatPausedEvent>();

            var parked = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(run.Id));
            parked.Status.ShouldBe(ChatRunStatus.Paused);

            var resumed = await _service.ResumeRunAsync(conversation.Id, run.Id, CancellationToken.None);
            resumed.Status.ShouldBe("paused", "o executor parked ainda vai persistir o resume");

            while (await enumerator.MoveNextAsync())
            {
                events.Add(enumerator.Current);
            }

            events.OfType<ChatResumedEvent>().ShouldHaveSingleItem();
            events.OfType<ChatDoneEvent>().ShouldHaveSingleItem();
        }
        finally
        {
            _coordinator.End(conversation.Id, runCts);
            runCts.Dispose();
        }
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
        // RF-007 (context-management): a pressão sai antes da 1ª chamada ao provider.
        enumerator.Current.ShouldBeOfType<ChatPressureEvent>();
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

        var row = await _context.ChatApprovals.AsNoTracking().SingleAsync(a => a.Id == approval.Id);
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

        var row = await _context.ChatApprovals.AsNoTracking().SingleAsync();
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

        var row = await _context.ChatApprovals.AsNoTracking().SingleAsync(a => a.Id == approval.Id);
        row.Status.ShouldBe(ChatApprovalStatus.Cancelled);
        row.DecidedBy.ShouldBe(ChatApprovalDecidedBy.AutoCancel);
        tool.Executions.ShouldBe(0);
    }

    // ---- SPEC-20261013-chat-risk-approvals: preset `auto` no gate ----

    /// <summary>Tool mutante sem RequiresConfirmation — o auto decide por risco.</summary>
    private sealed class FakeRunTestsTool : IChatTool
    {
        public string Name => "run_tests";
        public string Description => "runs tests";
        public string ParametersJson => """{"type":"object","properties":{}}""";
        public int Executions { get; private set; }

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { passed = 1 })));
        }
    }

    /// <summary>shell_exec fake — comandos classificados pela tabela de risco.</summary>
    private sealed class FakeShellExecTool : IChatTool
    {
        public string Name => "shell_exec";
        public string Description => "shell";
        public string ParametersJson => """{"type":"object","properties":{"command":{"type":"string"}}}""";
        public int Executions { get; private set; }

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { exitCode = 0 })));
        }
    }

    /// <summary>RF-005: RequiresConfirmation nunca é rebaixado — auto ainda pergunta.</summary>
    [Fact]
    public async Task Dado_PresetAuto_Quando_ToolExigeConfirmacao_Entao_Pede()
    {
        var tool = new FakeWriteTool();
        var service = NewApprovalService(tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "auto"), CancellationToken.None);

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);
        approval.ToolName.ShouldBe("write_file");
        approval.Risk.ShouldBeNull("hard gate não vem do classifier");

        await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("allow"), CancellationToken.None);
        await runTask;
        tool.Executions.ShouldBe(1);
    }

    /// <summary>RF-004: medium roda com risk.notice + linha de auditoria auto:medium.</summary>
    [Fact]
    public async Task Dado_PresetAuto_Quando_ToolMedium_Entao_ExecutaComNoticeEAuditoria()
    {
        var tool = new FakeRunTestsTool();
        var service = NewService(
            new PlanReviewProviderHandler("run_tests", "{}"),
            new ChatRunCoordinator(), extraTool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "auto"), CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "testa");

        tool.Executions.ShouldBe(1);
        var notice = events.OfType<ChatRiskNoticeEvent>().Single();
        notice.ToolName.ShouldBe("run_tests");
        notice.Risk.ShouldBe("medium");
        var audit = await _context.ChatApprovals.AsNoTracking()
            .SingleAsync(a => a.ConversationId == ChatConversationId.From(conversation.Id));
        audit.Status.ShouldBe(ChatApprovalStatus.AllowedOnce);
        audit.DecidedBy!.Value.ShouldBe("auto:medium");
        audit.Risk.ShouldBe("medium");
        audit.RiskReason.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>RF-004: comando destrutivo abre o ask card com risk: high + razão.</summary>
    [Fact]
    public async Task Dado_PresetAuto_Quando_ComandoDestrutivo_Entao_AskCardComRisco()
    {
        var tool = new FakeShellExecTool();
        var service = NewService(
            new PlanReviewProviderHandler("shell_exec", """{"command":"rm -rf /tmp/x"}"""),
            new ChatRunCoordinator(), extraTool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "auto"), CancellationToken.None);

        var runTask = RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "limpa");
        var approval = await WaitForPendingApprovalAsync(conversation.Id);
        approval.ToolName.ShouldBe("shell_exec");
        approval.Risk.ShouldBe("high");
        approval.RiskReason.ShouldNotBeNull().ShouldContain("rm -rf");

        await service.DecideApprovalAsync(
            approval.Id.Value, new DecideChatApprovalRequest("deny"), CancellationToken.None);
        var events = await runTask;
        tool.Executions.ShouldBe(0);
        events.OfType<ChatToolResultEvent>().Single().Refused.ShouldBeTrue();
    }

    /// <summary>RF-004: leitura sob auto roda em silêncio — zero approval/notice.</summary>
    [Fact]
    public async Task Dado_PresetAuto_Quando_ToolLeitura_Entao_Silencioso()
    {
        var service = NewService(
            new PlanReviewProviderHandler("echo_tool", """{"text":"oi"}"""),
            new ChatRunCoordinator());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "auto"), CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "leia");

        events.OfType<ChatRiskNoticeEvent>().ShouldBeEmpty();
        _context.ChatApprovals.ShouldBeEmpty();
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("echo:oi");
    }

    /// <summary>RNF-001: plan mode continua lei — auto não rebaixa a recusa.</summary>
    [Fact]
    public async Task Dado_PlanModeOn_Quando_PresetAuto_Entao_MutanteNegado()
    {
        var tool = new FakeWriteTool();
        var service = NewPlanService(new MutatingProviderHandler(), tool: tool);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await service.PatchConversationAsync(
            conversation.Id, new PatchChatConversationRequest(PermissionPreset: "auto"), CancellationToken.None);
        await service.SetPlanModeAsync(conversation.Id, true, CancellationToken.None);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "escreva");

        tool.Executions.ShouldBe(0);
        _context.ChatApprovals.ShouldBeEmpty();
        events.OfType<ChatToolResultEvent>().Single().ResultJson
            .ShouldContain("Plan mode is active");
    }

    /// <summary>RF-003: override por tool `auto` sob preset chat decide por risco.</summary>
    [Fact]
    public async Task Dado_ToolPolicyAuto_Quando_PresetChat_Entao_MediumExecuta()
    {
        var tool = new FakeRunTestsTool();
        var service = NewService(
            new PlanReviewProviderHandler("run_tests", "{}"),
            new ChatRunCoordinator(), extraTool: tool,
            extraConfig: new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Approval:ToolPolicy:run_tests"] = "auto",
            });
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        // preset continua o default ask — a policy por tool vence.
        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "testa");

        tool.Executions.ShouldBe(1);
        events.OfType<ChatRiskNoticeEvent>().Single().Risk.ShouldBe("medium");
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

    [Fact]
    public async Task Dado_ProviderDeletadoForaDoService_Quando_CriarConversa_Entao_SnapshotServidoAteInvalidacaoPorTag()
    {
        // Outro DbContext no mesmo SQLite — sem identity-map aliasing do EF:
        // a leitura no service é DB de verdade, só o snapshot L1 pode salvar.
        var service = NewService(new FakeProviderHandler(), _coordinator, context: NewSecondContext());
        (await service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1")))
            .ProviderName.ShouldBe("fake");

        // Delete por fora do service não invalida — o snapshot segue servindo.
        _context.ChatProviders.Where(p => p.Id == _provider.Id).ExecuteDelete();
        (await service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1")))
            .ProviderName.ShouldBe("fake");

        // Write via service em outro provider derruba a tag → próxima leitura
        // refaz a query e enxerga a deleção.
        var other = ChatProvider.Create("other", "http://other.test", "sk");
        _context.ChatProviders.Add(other);
        _context.SaveChanges();
        await service.UpdateProviderAsync(other.Id, new ChatProviderUpsertRequest("other2", "http://other.test"));
        await Should.ThrowAsync<ChatValidationException>(
            async () => await service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1")));
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

    // ---- Context management (SPEC-20261005-chat-context-management) ----

    /// <summary>Provider fake que grava os bodies e decide a resposta por call-index.</summary>
    private sealed class ScriptedProviderHandler(Func<int, (string Body, HttpStatusCode Status)> respond) : HttpMessageHandler
    {
        private int _calls;

        public List<string> Bodies { get; } = [];

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var (body, status) = respond(Interlocked.Increment(ref _calls));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static string OkSse(string content = "ok") =>
        """
        data: {"choices":[{"delta":{"content":"@C@"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}
        data: [DONE]
        """.Replace("@C@", content, StringComparison.Ordinal);

    /// <summary>Tool que devolve um resultado gigante (força o spill do RF-006).</summary>
    private sealed class BigResultTool : IChatTool
    {
        public string Name => "big_tool";
        public string Description => "big";
        public string ParametersJson => """{"type":"object","properties":{}}""";

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { output = new string('x', 40_000) })));
    }

    /// <summary>Semeia N turnos user+tool-result com marcador reconhecível.</summary>
    private async Task SeedHistoryAsync(string conversationId, int turns, string marker)
    {
        var convId = ChatConversationId.From(conversationId);
        var t0 = DateTime.UtcNow.AddMinutes(-turns);
        for (var i = 0; i < turns; i++)
        {
            _context.ChatMessages.Add(ChatMessage.CreateUser(
                convId, $"pedido-{i}-{marker}", t0.AddSeconds(i * 10)));
            _context.ChatMessages.Add(ChatMessage.CreateTool(
                convId, $"call_{i}", "echo_tool",
                $$"""{"output":"{{marker}}-resultado-{{i}}-{{new string('x', 300)}}"}""",
                false, t0.AddSeconds(i * 10 + 1)));
        }

        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Dado_HistoricoAcimaDoLimite_Quando_Enviar_Entao_PodaResultadosAntigos()
    {
        var handler = new ScriptedProviderHandler(_ => (OkSse(), HttpStatusCode.OK));
        var service = NewService(handler, new ChatRunCoordinator(), extraConfig: new()
        {
            ["Taskboard:Chat:Context:CompactAtTokens"] = "400",
            ["Taskboard:Chat:Context:KeepRecentTurns"] = "2",
        });
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await SeedHistoryAsync(conversation.Id, 5, "velho");

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        // RF-003: wire carrega tombstone nos resultados antigos; o último turno fica inteiro.
        handler.Bodies.ShouldNotBeEmpty();
        var request = handler.Bodies[0];
        request.ShouldContain("earlier tool output pruned");
        request.ShouldNotContain("velho-resultado-0-");
        request.ShouldNotContain("velho-resultado-3-");
        request.ShouldContain("velho-resultado-4-");

        // RF-007: evento do medidor + stats na run.
        events.OfType<ChatPressureEvent>().ShouldNotBeEmpty();
        var run = await _context.ChatRuns.OrderBy(r => r.CreatedAt).LastAsync();
        run.CompactionCount.ShouldBeGreaterThanOrEqualTo(1);
        run.ContextTokensLimit.ShouldBe(400);

        // RNF-001: o histórico persistido NUNCA é alterado.
        _context.ChatMessages.Count(m => m.Content.Contains("velho-resultado-0-")).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_ResultadoGigante_Quando_Podado_Entao_SpillComPonteiro()
    {
        var spillDir = Path.Join(Path.GetTempPath(), $"tb-spill-{Guid.NewGuid()}");
        var store = new Taskboard.Integrations.Chat.ChatSpillStore(spillDir);
        var handler = new ScriptedProviderHandler(_ => (OkSse(), HttpStatusCode.OK));
        var service = NewService(handler, new ChatRunCoordinator(), extraConfig: new()
        {
            ["Taskboard:Chat:Context:CompactAtTokens"] = "400",
            ["Taskboard:Chat:Context:KeepRecentTurns"] = "1",
            ["Taskboard:Chat:Context:SpillBytes"] = "1000",
            ["Taskboard:Chat:Context:SpillHeadBytes"] = "100",
        }, spillStore: store);
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        // 2 turnos antigos com resultado grande → os dois viram spill (não tombstone).
        var convId = ChatConversationId.From(conversation.Id);
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 2; i++)
        {
            _context.ChatMessages.Add(ChatMessage.CreateUser(convId, $"u{i}", t0.AddSeconds(i * 10)));
            _context.ChatMessages.Add(ChatMessage.CreateTool(
                convId, $"call_{i}", "echo_tool",
                $$"""{"output":"big-{{i}}-{{new string('y', 5000)}}"}""",
                false, t0.AddSeconds(i * 10 + 1)));
        }

        await _context.SaveChangesAsync();

        await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        var request = handler.Bodies[0];
        request.ShouldContain("spill://");
        request.ShouldContain("big-0-");
        request.ShouldNotContain(new string('y', 5000));
        Directory.Exists(Path.Join(spillDir, "spill")).ShouldBeTrue();
        Directory.GetFiles(Path.Join(spillDir, "spill"), "*", SearchOption.AllDirectories)
            .ShouldNotBeEmpty("RF-006: arquivo de spill escrito");
    }

    [Fact]
    public async Task Dado_ResumoPersistido_Quando_ProximaRun_Entao_WireUsaResumoESaltaAntigos()
    {
        var handler = new ScriptedProviderHandler(_ => (OkSse("RESUMO"), HttpStatusCode.OK));
        var service = NewService(handler, new ChatRunCoordinator(), extraConfig: new()
        {
            // limite mínimo → prune + summarize a cada turno.
            ["Taskboard:Chat:Context:CompactAtTokens"] = "1",
            ["Taskboard:Chat:Context:KeepRecentTurns"] = "2",
        });
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await SeedHistoryAsync(conversation.Id, 5, "velho");

        await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        _context.ChatMessages.Any(m => m.Kind == "summary")
            .ShouldBeTrue("RF-004: resumo persistido como system+summary");

        await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "mais");

        var request = handler.Bodies[^1];
        request.ShouldContain("context compacted");
        // RNF-001: a wire salta o prefixo coberto pelo resumo.
        request.ShouldNotContain("velho-resultado-0-");
    }

    [Fact]
    public async Task Dado_ContextLengthExceeded_Quando_ProviderRetorna_Entao_RetentaUmaVez()
    {
        var handler = new ScriptedProviderHandler(call => call == 1
            ? ("""{"error":{"message":"context_length_exceeded"}}""", HttpStatusCode.BadRequest)
            : (OkSse("ok-depois"), HttpStatusCode.OK));
        var service = NewService(handler, new ChatRunCoordinator());

        var events = await RunTurnAsync(service, new ChatRunCoordinator(),
            (await service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1"))).Id,
            "oi");

        handler.Calls.ShouldBe(2, "RF-005: um retry após context_length_exceeded");
        events.OfType<ChatDeltaEvent>().Any(d => d.Content.Contains("ok-depois")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ContextLengthExceededPersistente_Quando_Retenta_Entao_Falha()
    {
        var handler = new ScriptedProviderHandler(_ =>
            ("""{"error":{"message":"context_length_exceeded"}}""", HttpStatusCode.BadRequest));
        var service = NewService(handler, new ChatRunCoordinator());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        handler.Calls.ShouldBe(2, "RF-005: retry único — a segunda falha encerra");
        var done = events.OfType<ChatDoneEvent>().LastOrDefault();
        done.ShouldNotBeNull();
        done.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_SpillGuardado_Quando_ReadFileComSpillUri_Entao_LePaginado()
    {
        var dir = Path.Join(Path.GetTempPath(), $"tb-spill-{Guid.NewGuid()}");
        var store = new Taskboard.Integrations.Chat.ChatSpillStore(dir);
        var content = string.Concat(Enumerable.Range(0, 2000).Select(i => $"{i % 10}"));
        var spillId = store.Save("run-1", 1, content);

        var tool = new Taskboard.Integrations.Chat.Tools.ReadFileTool(
            new Taskboard.Integrations.Harness.Security.SecretScrubber(), store);
        var args = JsonDocument.Parse(
            $$"""{"path":"spill://{{spillId}}","offset":100,"limit":50}""").RootElement;
        var context = new ChatToolContext(
            WorkspacePath: dir, ProviderId: Guid.NewGuid(), ProviderBaseUrl: "http://x",
            ProviderApiKey: "k", ImageModel: "", SearchBackend: "none",
            SearchUrl: "", SearchApiKey: "");

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("totalChars").GetInt32().ShouldBe(2000);
        doc.RootElement.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("content").GetString().ShouldBe(content.Substring(100, 50));
    }

    [Fact]
    public async Task Dado_SpillUriInvalida_Quando_ReadFile_Entao_ErroSemTraversal()
    {
        var dir = Path.Join(Path.GetTempPath(), $"tb-spill-{Guid.NewGuid()}");
        var store = new Taskboard.Integrations.Chat.ChatSpillStore(dir);
        var tool = new Taskboard.Integrations.Chat.Tools.ReadFileTool(
            new Taskboard.Integrations.Harness.Security.SecretScrubber(), store);
        var context = new ChatToolContext(
            WorkspacePath: dir, ProviderId: Guid.NewGuid(), ProviderBaseUrl: "http://x",
            ProviderApiKey: "k", ImageModel: "", SearchBackend: "none",
            SearchUrl: "", SearchApiKey: "");

        var bad = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"path":"spill://../etc/passwd"}""").RootElement, context, CancellationToken.None);
        bad.Json.ShouldContain("not found");

        // RNF-003: id com separador nunca resolve um arquivo fora do spill dir.
        var traversal = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"path":"spill://a-../../x"}""").RootElement, context, CancellationToken.None);
        traversal.Json.ShouldContain("not found");
    }

    [Fact]
    public void Dado_SpillStore_Quando_SweepOrphans_Entao_RemoveApenasRunsMortas()
    {
        var dir = Path.Join(Path.GetTempPath(), $"tb-spill-{Guid.NewGuid()}");
        var store = new Taskboard.Integrations.Chat.ChatSpillStore(dir);
        store.Save("alive", 1, "x");
        store.Save("dead-1", 1, "x");
        store.Save("dead-2", 1, "x");

        var swept = store.SweepOrphans(new HashSet<string> { "alive" });

        swept.ShouldBe(2);
        Directory.Exists(Path.Join(dir, "spill", "alive")).ShouldBeTrue();
        Directory.Exists(Path.Join(dir, "spill", "dead-1")).ShouldBeFalse();
    }

    [Fact]
    public void Dado_SpillStore_Quando_DeleteRunDir_Entao_RemoveDiretorio()
    {
        var dir = Path.Join(Path.GetTempPath(), $"tb-spill-{Guid.NewGuid()}");
        var store = new Taskboard.Integrations.Chat.ChatSpillStore(dir);
        var id = store.Save("run-x", 1, "x");

        store.DeleteRunDir("run-x");

        store.Read(id).ShouldBeNull();
        Directory.Exists(Path.Join(dir, "spill", "run-x")).ShouldBeFalse();
    }

    // ---- Fork & steering (SPEC-20261005-chat-fork-steering) ----

    /// <summary>Semeia uma run "Running" (status que o dispatcher grava).</summary>
    private async Task<ChatRun> SeedRunningRunAsync(string conversationId, string trigger = "vai")
    {
        var convId = ChatConversationId.From(conversationId);
        var user = ChatMessage.CreateUser(convId, trigger, DateTime.UtcNow);
        _context.ChatMessages.Add(user);
        var run = ChatRun.Create(ChatRunId.NewGuid(), convId, user.Id, DateTime.UtcNow);
        _context.ChatRuns.Add(run);
        await _context.SaveChangesAsync();
        run.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task Dado_ConversaComMensagens_Quando_Fork_Entao_CopiaPrefixoComNovosIdsELinhagem()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1", Title: "origem"));
        var convId = ChatConversationId.From(conversation.Id);
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "m-um", t0));
        _context.ChatMessages.Add(ChatMessage.CreateAssistant(convId, "m-dois", null, null, null, "m1", t0.AddSeconds(1)));
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "m-tres", t0.AddSeconds(2)));
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "m-quatro", t0.AddSeconds(3)));
        await _context.SaveChangesAsync();
        var bound = _context.ChatMessages
            .Where(m => m.ConversationId == convId).OrderBy(m => m.CreatedAt).Skip(1).First();

        var fork = await _service.ForkConversationAsync(conversation.Id, bound.Id.Value);

        fork.ForkedFromConversationId.ShouldBe(conversation.Id);
        fork.ForkedAtMessageId.ShouldBe(bound.Id.Value);
        fork.Title.ShouldBe("origem (fork)");
        var copied = _context.ChatMessages
            .Where(m => m.ConversationId == ChatConversationId.From(fork.Id))
            .OrderBy(m => m.CreatedAt).ToList();
        // RF-001: prefixo inclusivo (m-um, m-dois) — o resto não copia.
        copied.Select(m => m.Content).ShouldBe(["m-um", "m-dois"]);
        copied.ShouldAllBe(m => m.ForkedFromMessageId != null);
        copied.Select(m => m.Id.Value).ShouldNotContain(bound.Id.Value);
        // RF-003: runs/approvals não copiam — a fork nasce idle.
        _context.ChatRuns.Count(r => r.ConversationId == ChatConversationId.From(fork.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Dado_Fork_Quando_Listar_Entao_SourceGanhaForkCount()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var convId = ChatConversationId.From(conversation.Id);
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "alvo", DateTime.UtcNow));
        await _context.SaveChangesAsync();
        var bound = _context.ChatMessages.Single(m => m.ConversationId == convId);

        await _service.ForkConversationAsync(conversation.Id, bound.Id.Value);

        var list = await _service.ListConversationsAsync(null);
        list.Single(c => c.Id == conversation.Id).ForkCount.ShouldBe(1);
        list.Single(c => c.ForkedFromConversationId == conversation.Id).Title.ShouldContain("(fork)");
    }

    [Fact]
    public async Task Dado_MessageIdDeOutraConversa_Quando_Fork_Entao_BadRequest()
    {
        var a = await _service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1"));
        var b = await _service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1"));
        _context.ChatMessages.Add(ChatMessage.CreateUser(
            ChatConversationId.From(b.Id), "de-b", DateTime.UtcNow));
        await _context.SaveChangesAsync();
        var foreign = _context.ChatMessages.Single(m => m.ConversationId == ChatConversationId.From(b.Id));

        // RF-002: messageId tem que pertencer à conversa — 400.
        await Should.ThrowAsync<ChatValidationException>(
            async () => await _service.ForkConversationAsync(a.Id, foreign.Id.Value));
    }

    [Fact]
    public async Task Dado_ConversaComSummary_Quando_Fork_Entao_RemapaSupersedesUntil()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var convId = ChatConversationId.From(conversation.Id);
        var t0 = DateTime.UtcNow.AddMinutes(-3);
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "old-1", t0));
        var old2 = ChatMessage.CreateUser(convId, "old-2", t0.AddSeconds(1));
        _context.ChatMessages.Add(old2);
        _context.ChatMessages.Add(ChatMessage.CreateSummary(convId, "resumo", old2.Id.Value, t0.AddSeconds(2)));
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "depois", t0.AddSeconds(3)));
        await _context.SaveChangesAsync();

        var boundId = _context.ChatMessages
            .Where(m => m.ConversationId == convId)
            .OrderBy(m => m.CreatedAt).Last().Id.Value;
        var fork = await _service.ForkConversationAsync(conversation.Id, boundId);

        var copiedSummary = _context.ChatMessages
            .Where(m => m.ConversationId == ChatConversationId.From(fork.Id) && m.Kind == "summary")
            .Single();
        var copiedBound = _context.ChatMessages
            .Where(m => m.ConversationId == ChatConversationId.From(fork.Id)
                && m.ForkedFromMessageId == old2.Id.Value)
            .Single();
        // O bound aponta para a CÓPIA do old-2, não para a linha original.
        copiedSummary.SupersedesUntilMessageId.ShouldBe(copiedBound.Id.Value);
    }

    [Fact]
    public async Task Dado_ConversaEmPlanMode_Quando_Fork_Entao_CopiaModoEPresetSemRun()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var convId = ChatConversationId.From(conversation.Id);
        var entity = await _context.ChatConversations.SingleAsync(c => c.Id == convId);
        entity.SetPlanMode(true, DateTime.UtcNow);
        entity.SetPermissionPreset("full", DateTime.UtcNow);
        _context.ChatMessages.Add(ChatMessage.CreateUser(convId, "alvo", DateTime.UtcNow));
        await _context.SaveChangesAsync();

        var fork = await _service.ForkConversationAsync(conversation.Id,
            _context.ChatMessages.Single(m => m.ConversationId == convId).Id.Value);
        var forkEntity = await _context.ChatConversations
            .SingleAsync(c => c.Id == ChatConversationId.From(fork.Id));

        // RF-003: modo/preset copiam; runs e approvals não.
        forkEntity.PlanMode.ShouldBe("on");
        forkEntity.PermissionPreset.ShouldBe("full");
    }

    [Fact]
    public async Task Dado_SemRunAtiva_Quando_EnqueueSteer_Entao_DegradaParaNull()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));

        // RF-005: steer sem run ativa → null (o caller faz o send normal).
        var result = await _service.EnqueueSteerAsync(conversation.Id, "corrige o caminho");

        result.ShouldBeNull();
        _context.ChatSteers.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Dado_RunAtiva_Quando_EnqueueSteer_Entao_CriaSteerPendenteNaRun()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await SeedRunningRunAsync(conversation.Id);

        var result = await _service.EnqueueSteerAsync(conversation.Id, "  troca de arquivo  ");

        result.ShouldNotBeNull();
        result.Value.Run.Id.ShouldBe(run.Id.Value);
        var steer = _context.ChatSteers.Single();
        steer.RunId.ShouldBe(run.Id);
        steer.Content.ShouldBe("troca de arquivo");
        steer.ClaimedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_InboxCheia_Quando_EnqueueSteer_Entao_Conflito409()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await SeedRunningRunAsync(conversation.Id);
        for (var i = 0; i < ChatSteer.MaxPendingPerRun; i++)
        {
            _context.ChatSteers.Add(ChatSteer.Create(
                ChatSteerId.NewGuid(), run.Id, run.ConversationId, $"s-{i}", DateTime.UtcNow.AddSeconds(i)));
        }

        await _context.SaveChangesAsync();

        await Should.ThrowAsync<ChatSteerConflictException>(
            async () => await _service.EnqueueSteerAsync(conversation.Id, "mais um"));
    }

    [Fact]
    public async Task Dado_SteerPendente_Quando_BoundaryDeToolResult_Entao_ClaimNaMesmaRun()
    {
        var convIdHolder = new ChatConversationId[1];
        var handler = new ScriptedProviderHandler(call =>
        {
            if (call == 1)
            {
                // "Enfileira" o steer no meio da run — entre a resposta do
                // provider e o boundary de tool-result (ordem determinística).
                var convId = convIdHolder[0];
                var run = _context.ChatRuns.Single(r => r.ConversationId == convId);
                _context.ChatSteers.Add(ChatSteer.Create(
                    ChatSteerId.NewGuid(), run.Id, convId, "conteudo-steer", DateTime.UtcNow));
                _context.SaveChanges();
                return ("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"echo_tool","arguments":"{}"}}]}}]}""" + "\ndata: [DONE]\n",
                    HttpStatusCode.OK);
            }

            return (OkSse("depois"), HttpStatusCode.OK);
        });
        var service = NewService(handler, new ChatRunCoordinator());
        var conversation = await service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        convIdHolder[0] = ChatConversationId.From(conversation.Id);

        var events = await RunTurnAsync(service, new ChatRunCoordinator(), conversation.Id, "oi");

        // RF-006: o steer virou mensagem user Kind=steer e entrou no wire ANTES
        // da 2ª chamada ao modelo.
        var claimed = events.OfType<ChatSteerClaimedEvent>().ShouldHaveSingleItem();
        claimed.Content.ShouldBe("conteudo-steer");
        handler.Calls.ShouldBe(2);
        handler.Bodies[1].ShouldContain("conteudo-steer");
        _context.ChatMessages
            .Any(m => m.Kind == "steer" && m.Role == ChatMessageRole.User).ShouldBeTrue();
        // O claim vai ao banco via ExecuteUpdate — recarrega sem o tracker stale.
        _context.ChangeTracker.Clear();
        _context.ChatSteers.Single().ClaimedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_SteerPendente_Quando_Cancel_Entao_RemoveLinha()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await SeedRunningRunAsync(conversation.Id);
        var steer = ChatSteer.Create(ChatSteerId.NewGuid(), run.Id, run.ConversationId, "sai", DateTime.UtcNow);
        _context.ChatSteers.Add(steer);
        await _context.SaveChangesAsync();

        await _service.CancelSteerAsync(conversation.Id, steer.Id.Value);

        _context.ChatSteers.Count().ShouldBe(0);
        await Should.ThrowAsync<ChatValidationException>(
            async () => await _service.CancelSteerAsync(conversation.Id, steer.Id.Value));
    }

    [Fact]
    public async Task Dado_SteerClaimed_Quando_Cancel_Entao_Conflito409()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await SeedRunningRunAsync(conversation.Id);
        var steer = ChatSteer.Create(ChatSteerId.NewGuid(), run.Id, run.ConversationId, "tarde", DateTime.UtcNow);
        _context.ChatSteers.Add(steer);
        await _context.SaveChangesAsync();
        steer.Claim(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        // RF-007: já claimed — o cancel responde 409.
        await Should.ThrowAsync<ChatSteerConflictException>(
            async () => await _service.CancelSteerAsync(conversation.Id, steer.Id.Value));
    }

    [Fact]
    public async Task Dado_SteerDeOutraConversa_Quando_Cancel_Entao_NotFound()
    {
        var a = await _service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1"));
        var b = await _service.CreateConversationAsync(new CreateChatConversationRequest(_provider.Id, "m1"));
        var run = await SeedRunningRunAsync(b.Id);
        var steer = ChatSteer.Create(ChatSteerId.NewGuid(), run.Id, run.ConversationId, "x", DateTime.UtcNow);
        _context.ChatSteers.Add(steer);
        await _context.SaveChangesAsync();

        await Should.ThrowAsync<ChatValidationException>(
            async () => await _service.CancelSteerAsync(a.Id, steer.Id.Value));
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
