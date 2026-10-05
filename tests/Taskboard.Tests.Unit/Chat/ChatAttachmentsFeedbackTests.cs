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
using Taskboard.Integrations.Chat;
using Taskboard.Integrations.Harness;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-attachments-feedback: sniffer/store/tracker puros +
/// ciclo do serviço em SQLite (upload→bind→download, feedback CAS, filtro 👎,
/// deliverables no fim da run).
/// </summary>
public sealed class ChatAttachmentsFeedbackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dataDir;
    private readonly TaskboardDbContext _context;
    private readonly ChatProvider _provider;
    private readonly List<HttpClient> _httpClients = [];
    private readonly ChatRunBroadcaster _broadcaster = new();
    private readonly ChatRunQueue _runQueue = new();
    private readonly ChatRunCoordinator _coordinator = new();
    private readonly ChatApprovalCoordinator _approvalCoordinator = new();
    private readonly List<IChatRunNotifier> _notifiers = [];
    private readonly List<TaskboardDbContext> _extraContexts = [];
    private readonly ChatAttachmentStore _store;
    private readonly ChatFileEditTracker _tracker = new();

    public ChatAttachmentsFeedbackTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-chat-att-{Guid.NewGuid()}.sqlite");
        _dataDir = Path.Join(Path.GetTempPath(), $"tb-att-{Guid.NewGuid()}");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();
        _store = new ChatAttachmentStore(_dataDir);
    }

    private ChatService NewService(
        HttpMessageHandler handler,
        ChatRunCoordinator coordinator,
        IChatWorkspaceDiffService? workspaceDiff = null,
        Dictionary<string, string?>? extraConfig = null)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Taskboard:Chat:Tools:Enabled"] = "true",
            ["Taskboard:Chat:MaxToolIterations"] = "4",
            ["Taskboard:Chat:Attachments:Enabled"] = "true",
            ["Taskboard:Chat:Attachments:MaxBytes"] = "1048576",
            ["Taskboard:Chat:Attachments:MaxPerMessage"] = "5",
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
        var client = new HttpClient(handler);
        _httpClients.Add(client);
        return new ChatService(
            new EfCoreRepository<ChatProvider>(_context),
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatMessage>(_context),
            new OpenAiCompatibleClient(client),
            new ChatCapabilityRegistry(tools, new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            new FakeWorkspaceResolver(),
            configuration,
            coordinator,
            NewHybridCache(),
            new EfCoreRepository<ChatRun>(_context),
            _runQueue,
            _broadcaster,
            new EfCoreRepository<ChatApproval>(_context),
            new EfCoreRepository<ChatSteer>(_context),
            _approvalCoordinator,
            _notifiers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatService>.Instance,
            attachmentRepository: new EfCoreRepository<ChatAttachment>(_context),
            feedbackRepository: new EfCoreRepository<ChatMessageFeedback>(_context),
            deliverableRepository: new EfCoreRepository<ChatRunDeliverable>(_context),
            attachmentStore: _store,
            editTracker: _tracker,
            workspaceDiff: workspaceDiff ?? new NullWorkspaceDiff());
    }

    private async Task<ChatConversationDto> NewConversationAsync(ChatService service)
    {
        var request = new CreateChatConversationRequest(_provider.Id, "fake-model");
        return await service.CreateConversationAsync(request);
    }

    private async Task<List<ChatStreamEvent>> RunTurnAsync(
        ChatService service, string conversationId, string content, IReadOnlyList<string>? attachments = null)
    {
        var run = await service.EnqueueMessageAsync(conversationId, content, attachmentIds: attachments);
        return await ExecuteRunAsync(service, conversationId, run.Id);
    }

    private async Task<List<ChatStreamEvent>> ExecuteRunAsync(
        ChatService service, string conversationId, string runId)
    {
        var row = await _context.ChatRuns.SingleAsync(r => r.Id == ChatRunId.From(runId));
        row.Start(DateTime.UtcNow);
        await _context.SaveChangesAsync();
        var runCts = await _coordinator.BeginAsync(conversationId);
        try
        {
            var events = new List<ChatStreamEvent>();
            await foreach (var chatEvent in service.ExecuteAsync(row, runCts, CancellationToken.None))
            {
                events.Add(chatEvent);
            }

            // Executor marks the row like ChatRunDispatcherService does —
            // terminal status is what GetConversationAsync uses for LastRun.
            var done = events.OfType<ChatDoneEvent>().LastOrDefault();
            if (done?.Error is not null)
            {
                row.Fail(done.Error, DateTime.UtcNow);
            }
            else if (done is not null)
            {
                row.Complete(done.TokensIn, done.TokensOut, DateTime.UtcNow);
            }
            else
            {
                row.Fail("run ended without a terminal event", DateTime.UtcNow);
            }

            await _context.SaveChangesAsync();
            return events;
        }
        finally
        {
            _coordinator.End(conversationId, runCts);
            runCts.Dispose();
        }
    }

    private static HybridCache NewHybridCache() =>
        new ServiceCollection().AddHybridCache().Services.BuildServiceProvider()
            .GetRequiredService<HybridCache>();

    // ---- sniffer (RF-001/RNF-002) ----

    [Fact]
    public void Dado_BytesPng_Quando_Sniff_Entao_RetornaImagePng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        ChatAttachmentSniffer.Sniff(bytes, "x.bin", ChatAttachmentSniffer.DefaultAllowedMime)
            .ShouldBe("image/png");
    }

    [Fact]
    public void Dado_BinarioDesconhecido_Quando_Sniff_Entao_RetornaNull()
    {
        // MZ stub: decodable? has NULs → not text; no magic → null.
        var bytes = new byte[] { 0x4D, 0x5A, 0x00, 0x00, 0x03, 0x00 };
        ChatAttachmentSniffer.Sniff(bytes, "app.exe", ChatAttachmentSniffer.DefaultAllowedMime)
            .ShouldBeNull();
    }

    [Fact]
    public void Dado_PngMasWildcardFora_Quando_Sniff_Entao_RetornaNull()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        ChatAttachmentSniffer.Sniff(bytes, "x.png", ["text/*"])
            .ShouldBeNull();
    }

    [Fact]
    public void Dado_MarkdownUtf8_Quando_Sniff_Entao_TextMarkdown()
    {
        var bytes = Encoding.UTF8.GetBytes("# título\nplain text\n");
        ChatAttachmentSniffer.Sniff(bytes, "notes.md", ChatAttachmentSniffer.DefaultAllowedMime)
            .ShouldBe("text/markdown");
    }

    [Fact]
    public void Dado_SvgTexto_Quando_Sniff_Entao_ImageSvg()
    {
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        ChatAttachmentSniffer.Sniff(bytes, "icon.svg", ChatAttachmentSniffer.DefaultAllowedMime)
            .ShouldBe("image/svg+xml");
    }

    // ---- store (RF-001) ----

    [Fact]
    public void Dado_Bytes_Quando_Save_Entao_CriaArquivoEResolvePorId()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var (storagePath, sha) = _store.Save("abc123", "image/png", png);

        storagePath.ShouldBe("abc123.png");
        File.Exists(Path.Join(_store.Root, storagePath)).ShouldBeTrue();
        sha.Length.ShouldBe(64);
        var resolved = _store.Resolve("abc123");
        resolved.ShouldNotBeNull();
        resolved!.Value.ContentType.ShouldBe("image/png");
        File.ReadAllBytes(resolved.Value.FullPath).ShouldBe(png);
    }

    [Fact]
    public void Dado_IdInexistente_Quando_Resolve_Entao_Null() =>
        _store.Resolve("nope").ShouldBeNull();

    [Fact]
    public void Dado_PathTraversal_Quando_ResolvePath_Entao_Null() =>
        _store.ResolvePath("../evil.png").ShouldBeNull();

    // ---- file-edit tracker (RF-007) ----

    [Fact]
    public void Dado_EdicaoRepetida_Quando_Drain_Entao_CoalescePrimeiroAntesEUltimoDepois()
    {
        _tracker.BeforeEdit("r1", "c1", "src/a.cs", "v1");
        _tracker.AfterEdit("r1", "c1", "src/a.cs", "v2");
        _tracker.BeforeEdit("r1", "c1", "src/a.cs", "v2");
        _tracker.AfterEdit("r1", "c1", "src/a.cs", "v3");

        var edits = _tracker.Drain("r1");
        edits.ShouldHaveSingleItem().ShouldSatisfyAllConditions(e =>
        {
            e.Path.ShouldBe("src/a.cs");
            e.BeforeContent.ShouldBe("v1");
            e.AfterContent.ShouldBe("v3");
            e.Source.ShouldBe("tool-edit");
        });
        _tracker.Drain("r1").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_Declare_Quando_Drain_Entao_SourcePresentComSummary()
    {
        _tracker.Declare("r1", "c1", "report.md", "resumo da entrega");
        _tracker.Drain("r1").ShouldHaveSingleItem().ShouldSatisfyAllConditions(e =>
        {
            e.Source.ShouldBe("present");
            e.Summary.ShouldBe("resumo da entrega");
        });
    }

    // ---- git workspace diff (RF-007) ----

    [Fact]
    public async Task Dado_ForaDeGit_Quando_Snapshot_Entao_Null()
    {
        var git = new FakeGitRunner(new GitCommandResult(128, "", "not a repo", false));
        var svc = new GitWorkspaceDiffService(git);
        var dir = Directory.CreateTempSubdirectory().FullName;
        (await svc.SnapshotAsync(dir)).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_NumstatDeltaEUntrackedNovo_Quando_Diff_Entao_ListaArquivos()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Join(dir, "novo.cs"), "a\nb\n");
        var git = new FakeGitRunner(
            // DiffAsync order: numstat then porcelain.
            new GitCommandResult(0, "8\t3\tsrc/velho.cs\n", "", false),
            new GitCommandResult(0, "?? novo.cs\0?? ja_tinha.cs\0", "", false));
        var svc = new GitWorkspaceDiffService(git);
        var snapshot = new ChatWorkspaceSnapshot(
            Porcelain: "?? ja_tinha.cs\0",
            Numstat: "5\t3\tsrc/velho.cs\n");

        var rows = await svc.DiffAsync(dir, snapshot);
        rows.Count.ShouldBe(2);
        var old = rows.Single(r => r.Path == "src/velho.cs");
        old.AddedLines.ShouldBe(3); // delta 8−5
        old.RemovedLines.ShouldBe(0);
        var added = rows.Single(r => r.Path == "novo.cs");
        added.AddedLines.ShouldBe(2);
        added.Source.ShouldBe(ChatDeliverableSources.Git);
    }

    // ---- service: upload/bind/download (RF-001/RF-002/RF-004) ----

    [Fact]
    public async Task Dado_Upload_Quando_EnqueueComIds_Entao_VinculaNaMensagem()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 };
        var staged = await service.UploadAttachmentAsync(conv.Id, "shot.png", png);

        var run = await service.EnqueueMessageAsync(conv.Id, "olha isso", attachmentIds: [staged.Id]);

        var detail = await service.GetConversationAsync(conv.Id);
        detail.ShouldNotBeNull();
        var user = detail!.Messages.Single(m => m.Role == "user");
        user.Attachments.ShouldNotBeNull();
        user.Attachments!.ShouldHaveSingleItem().FileName.ShouldBe("shot.png");
        var row = await _context.ChatAttachments.SingleAsync();
        row.MessageId.ShouldBe(ChatMessageId.From(user.Id));
        run.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_UploadAcimaDoCap_Quando_Upload_Entao_400()
    {
        var service = NewService(
            new PlainTextProviderHandler(), _coordinator,
            extraConfig: new() { ["Taskboard:Chat:Attachments:MaxBytes"] = "4" });
        var conv = await NewConversationAsync(service);
        var act = () => service.UploadAttachmentAsync(conv.Id, "a.txt", "texto"u8.ToArray());
        await act.ShouldThrowAsync<ChatValidationException>();
    }

    [Fact]
    public async Task Dado_MimeForaDaLista_Quando_Upload_Entao_400()
    {
        var service = NewService(
            new PlainTextProviderHandler(), _coordinator,
            extraConfig: new() { ["Taskboard:Chat:Attachments:AllowedMime"] = "text/*" });
        var conv = await NewConversationAsync(service);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 };
        var act = () => service.UploadAttachmentAsync(conv.Id, "shot.png", png);
        await act.ShouldThrowAsync<ChatValidationException>();
    }

    [Fact]
    public async Task Dado_AnexoVinculado_Quando_Delete_Entao_409()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var staged = await service.UploadAttachmentAsync(conv.Id, "a.txt", "oi"u8.ToArray());
        await service.EnqueueMessageAsync(conv.Id, "com anexo", attachmentIds: [staged.Id]);

        var act = () => service.DeleteAttachmentAsync(conv.Id, staged.Id);
        await act.ShouldThrowAsync<ChatConflictException>();
    }

    [Fact]
    public async Task Dado_StagedOrfao_Quando_Sweep_Entao_RemoveRowEArquivo()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var staged = await service.UploadAttachmentAsync(conv.Id, "a.txt", "oi"u8.ToArray());
        var row = await _context.ChatAttachments.SingleAsync();
        // Envelhece além do cutoff — a row usa CreatedAt real; sweep com janela 0 pega tudo.
        row.ShouldNotBeNull();

        var swept = await service.SweepOrphanedAttachmentsAsync(TimeSpan.Zero);
        swept.ShouldBe(1);
        (await _context.ChatAttachments.CountAsync()).ShouldBe(0);
        _store.Resolve(staged.Id).ShouldBeNull();
    }

    // ---- service: feedback (RF-005) ----

    [Fact]
    public async Task Dado_AssistantMessage_Quando_PutFeedback_Entao_Persiste()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var assistant = ChatMessage.CreateAssistant(ChatConversationId.From(conv.Id), "resposta");
        _context.ChatMessages.Add(assistant);
        await _context.SaveChangesAsync();

        var dto = await service.PutMessageFeedbackAsync(assistant.Id.Value, "positive", null, null, 0);
        dto.Rating.ShouldBe("positive");
        dto.Version.ShouldBe(1);
        (await _context.ChatMessageFeedbacks.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_VersaoStale_Quando_PutFeedback_Entao_409()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var assistant = ChatMessage.CreateAssistant(ChatConversationId.From(conv.Id), "resposta");
        _context.ChatMessages.Add(assistant);
        await _context.SaveChangesAsync();

        var first = await service.PutMessageFeedbackAsync(assistant.Id.Value, "positive", null, null, 0);
        var act = () => service.PutMessageFeedbackAsync(assistant.Id.Value, "negative", null, null, expectedVersion: 0);
        await act.ShouldThrowAsync<ChatConflictException>();
        _ = first;
    }

    [Fact]
    public async Task Dado_UserMessage_Quando_PutFeedback_Entao_400()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var user = ChatMessage.CreateUser(ChatConversationId.From(conv.Id), "pergunta");
        _context.ChatMessages.Add(user);
        await _context.SaveChangesAsync();

        var act = () => service.PutMessageFeedbackAsync(user.Id.Value, "positive", null, null, 0);
        await act.ShouldThrowAsync<ChatValidationException>();
    }

    [Fact]
    public async Task Dado_FeedbackNegativo_Quando_ListConversations_Entao_Filtra()
    {
        var service = NewService(new PlainTextProviderHandler(), _coordinator);
        var conv = await NewConversationAsync(service);
        var other = await NewConversationAsync(service);
        var assistant = ChatMessage.CreateAssistant(ChatConversationId.From(conv.Id), "resposta");
        _context.ChatMessages.Add(assistant);
        await _context.SaveChangesAsync();
        await service.PutMessageFeedbackAsync(assistant.Id.Value, "negative", "wrong", null, 0);

        var filtered = await service.ListConversationsAsync(null, hasNegativeFeedback: true);
        filtered.ShouldHaveSingleItem().Id.ShouldBe(conv.Id);
        var all = await service.ListConversationsAsync(null);
        all.Count.ShouldBe(2);
        _ = other;
    }

    // ---- service: deliverables card (RF-007) ----

    [Fact]
    public async Task Dado_DiffDeWorkspace_Quando_RunTermina_Entao_PersisteEEmiteDeliverables()
    {
        var diff = new FakeWorkspaceDiff(
            [new ChatDeliverableDto("src/a.cs", 10, 2, ChatDeliverableSources.Git)]);
        var service = NewService(new PlainTextProviderHandler(), _coordinator, workspaceDiff: diff);
        var conv = await NewConversationAsync(service);

        var events = await RunTurnAsync(service, conv.Id, "faz a mudança");

        events.ShouldContain(e => e is ChatDeliverablesEvent);
        var rows = await _context.ChatRunDeliverables.ToListAsync();
        rows.ShouldHaveSingleItem().Path.ShouldBe("src/a.cs");
        var detail = await service.GetConversationAsync(conv.Id);
        detail!.LastRunDeliverables.ShouldNotBeNull();
        detail.LastRunDeliverables!.ShouldHaveSingleItem().Path.ShouldBe("src/a.cs");
    }

    [Fact]
    public async Task Dado_TrackerEdits_Quando_RunTermina_Entao_DedupeComGit()
    {
        // Tracker declarou tool-edit em src/a.cs; git também viu a.cs + b.cs —
        // a.cs fica com a fonte tool-edit, b.cs entra como git.
        var diff = new FakeWorkspaceDiff(
        [
            new ChatDeliverableDto("src/a.cs", 5, 1, ChatDeliverableSources.Git),
            new ChatDeliverableDto("src/b.cs", 3, 0, ChatDeliverableSources.Git),
        ]);
        var service = NewService(new PlainTextProviderHandler(), _coordinator, workspaceDiff: diff);
        var conv = await NewConversationAsync(service);
        // O tracker é keyed por runId — descobre o runId via enqueue+peek? O
        // tracker é chamado pelas tools durante a run; simulamos o estado como
        // se a tool tivesse rodado: escreve após enqueue (runId conhecido) e
        // antes do execute.
        var run = await service.EnqueueMessageAsync(conv.Id, "edita");
        _tracker.AfterEdit(run.Id, conv.Id, "src/a.cs", "novo");

        await ExecuteRunAsync(service, conv.Id, run.Id);

        var rows = await _context.ChatRunDeliverables.OrderBy(d => d.Path).ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Single(r => r.Path == "src/a.cs").Source.ShouldBe("tool-edit");
        rows.Single(r => r.Path == "src/b.cs").Source.ShouldBe("git");
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
        TryDelete(_dbPath);
        TryDelete($"{_dbPath}-shm");
        TryDelete($"{_dbPath}-wal");
        if (Directory.Exists(_dataDir))
        {
            try
            {
                Directory.Delete(_dataDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    // ---- fakes ----

    /// <summary>Provider fake: responde texto puro (sem tool calls) — turno de 1 chamada.</summary>
    private sealed class PlainTextProviderHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """data: {"choices":[{"delta":{"content":"ok"}}],"usage":{"prompt_tokens":2,"completion_tokens":1}}""" +
                    "\ndata: [DONE]\n",
                    Encoding.UTF8, "text/event-stream"),
            });
    }

    private sealed class FakeEchoTool : IChatTool
    {
        public string Name => "echo_tool";
        public string Description => "echo";
        public string ParametersJson => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { output = arguments.GetProperty("text").GetString() })));
    }

    /// <summary>Sem git — workspace fora de work tree (SnapshotAsync → null).</summary>
    private sealed class NullWorkspaceDiff : IChatWorkspaceDiffService
    {
        public Task<ChatWorkspaceSnapshot?> SnapshotAsync(string workspacePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatWorkspaceSnapshot?>(null);

        public Task<IReadOnlyList<ChatDeliverableDto>> DiffAsync(
            string workspacePath, ChatWorkspaceSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatDeliverableDto>>([]);
    }

    /// <summary>Diff fake: snapshot válido + respostas fixas no DiffAsync.</summary>
    private sealed class FakeWorkspaceDiff(IReadOnlyList<ChatDeliverableDto> deliverables) : IChatWorkspaceDiffService
    {
        public Task<ChatWorkspaceSnapshot?> SnapshotAsync(string workspacePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatWorkspaceSnapshot?>(new ChatWorkspaceSnapshot("", ""));

        public Task<IReadOnlyList<ChatDeliverableDto>> DiffAsync(
            string workspacePath, ChatWorkspaceSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult(deliverables);
    }

    /// <summary>Git runner fake: responde na ordem das chamadas.</summary>
    private sealed class FakeGitRunner(params GitCommandResult[] results) : IGitCommandRunner
    {
        private int _calls;

        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> arguments,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var index = Math.Min(_calls++, results.Length - 1);
            return Task.FromResult(results[index]);
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
}
