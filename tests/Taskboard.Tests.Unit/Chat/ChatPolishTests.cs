using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Chat;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261017-chat-polish: ⌘K command registry (canExecute gates), palette
/// fuzzy ordering, `ParseSuggestions` e `SuggestNextActionsAsync` — a chamada
/// de provider é barata e degradável (falha → lista vazia, nunca erro).
/// </summary>
public sealed class ChatPolishTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dataDir;
    private readonly TaskboardDbContext _context;
    private readonly ChatProvider _provider;
    private readonly ChatRunCoordinator _coordinator = new();
    private readonly ChatRunQueue _runQueue = new();
    private readonly ChatRunBroadcaster _broadcaster = new();
    private readonly List<IChatRunNotifier> _notifiers = [];
    private readonly List<HttpClient> _httpClients = [];

    public ChatPolishTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-polish-{Guid.NewGuid()}.sqlite");
        _dataDir = Path.Join(Path.GetTempPath(), $"tb-polish-{Guid.NewGuid()}");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        foreach (var client in _httpClients)
        {
            client.Dispose();
        }

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private ChatService NewService(HttpMessageHandler handler, ChatTodoStore? todoStore = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var client = new HttpClient(handler);
        _httpClients.Add(client);
        return new ChatService(
            new EfCoreRepository<ChatProvider>(_context),
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatMessage>(_context),
            new OpenAiCompatibleClient(client),
            new ChatCapabilityRegistry(new Dictionary<string, IChatTool>(), new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            new FakeWorkspaceResolver(),
            configuration,
            _coordinator,
            NewHybridCache(),
            new EfCoreRepository<ChatRun>(_context),
            _runQueue,
            _broadcaster,
            new EfCoreRepository<ChatApproval>(_context),
            new EfCoreRepository<ChatSteer>(_context),
            new ChatApprovalCoordinator(),
            _notifiers,
            NullLogger<ChatService>.Instance,
            todoStore: todoStore);
    }

    private static Microsoft.Extensions.Caching.Hybrid.HybridCache NewHybridCache() =>
        new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddHybridCache().Services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();

    private async Task<ChatConversation> NewConversationAsync()
    {
        var conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), _provider.Id, "fake", "m1", "polish test");
        _context.ChatConversations.Add(conversation);
        await _context.SaveChangesAsync();
        return conversation;
    }

    private void SeedAssistant(ChatConversation conversation, string content)
    {
        _context.ChatMessages.Add(ChatMessage.CreateAssistant(conversation.Id, content));
        _context.SaveChanges();
    }

    private sealed class SseHandler(string replyText, Action<string>? onBody = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                onBody?.Invoke(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var body = $"data: {{\"choices\":[{{\"delta\":{{\"content\":{System.Text.Json.JsonSerializer.Serialize(replyText)}}}}}]}}\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom"),
            });
    }

    // ------------------------------------------------------------------
    // RF-004: registry canExecute gates.
    // ------------------------------------------------------------------

    [Fact]
    public void Dado_SemConversa_Quando_Build_Entao_AcoesDeSessaoDesabilitadas()
    {
        var items = ChatCommands.Build(new ChatCommandState(
            HasConversation: false, RunActive: false, RunPaused: false, Archived: false));

        items.ShouldSatisfyAllConditions(
            () => items.Single(i => i.Id == "chat.new").Enabled.ShouldBeTrue(),
            () => items.Single(i => i.Id == "chat.archive").Enabled.ShouldBeFalse(),
            () => items.Single(i => i.Id == "chat.done").Enabled.ShouldBeFalse(),
            () => items.Single(i => i.Id == "chat.run.stop").Enabled.ShouldBeFalse(),
            () => items.Single(i => i.Id == "chat.run.pause").Enabled.ShouldBeFalse(),
            () => items.Where(i => i.Group == "workspace").ShouldAllBe(i => !i.Enabled),
            () => items.Where(i => i.Group == "app").ShouldAllBe(i => i.Enabled));
    }

    [Fact]
    public void Dado_RunAtiva_Quando_Build_Entao_StopEPauseHabilitados()
    {
        var items = ChatCommands.Build(new ChatCommandState(
            HasConversation: true, RunActive: true, RunPaused: false, Archived: false));

        items.Single(i => i.Id == "chat.run.stop").Enabled.ShouldBeTrue();
        items.Single(i => i.Id == "chat.run.pause").Enabled.ShouldBeTrue();
        items.Single(i => i.Id == "chat.archive").Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Dado_ConversaArquivada_Quando_Build_Entao_ArchiveEDoneDesabilitados()
    {
        var items = ChatCommands.Build(new ChatCommandState(
            HasConversation: true, RunActive: false, RunPaused: false, Archived: true));

        items.Single(i => i.Id == "chat.archive").Enabled.ShouldBeFalse();
        items.Single(i => i.Id == "chat.done").Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Dado_Build_Quando_Sempre_Entao_TodasAsAbasEPaginasRegistradas()
    {
        var items = ChatCommands.Build(new ChatCommandState(true, false, false, false));

        foreach (var tab in ChatCommands.WorkspaceTabs)
        {
            items.ShouldContain(i => i.Id == $"ws.tab.{tab}");
        }

        foreach (var (id, _) in ChatCommands.AppPages)
        {
            items.ShouldContain(i => i.Id == id);
        }
    }

    // ------------------------------------------------------------------
    // RF-003: palette fuzzy ordering.
    // ------------------------------------------------------------------

    private static string LabelOf(string key) => key switch
    {
        "chat.cmd.new" => "Nova conversa",
        "chat.cmd.stop" => "Parar run",
        "chat.cmd.tab.terminal" => "Abrir aba Terminal",
        "chat.cmd.settings-page" => "Ir para Settings",
        _ => key,
    };

    [Fact]
    public void Dado_QueryVazia_Quando_Filter_Entao_SessaoAntesDeWorkspace()
    {
        var items = ChatCommands.Build(new ChatCommandState(true, true, false, false));

        var filtered = ChatCommandPalette.Filter(items, LabelOf, "");

        filtered.ShouldNotBeEmpty();
        var firstWorkspace = filtered.ToList().FindIndex(i => i.Group == "workspace");
        var lastSession = filtered.ToList().FindLastIndex(i => i.Group == "session");
        (firstWorkspace == -1 || lastSession < firstWorkspace).ShouldBeTrue();
    }

    [Fact]
    public void Dado_QueryLabel_Quando_Filter_Entao_StartsWithPrimeiro()
    {
        var items = new List<ChatCommandItem>
        {
            new("a", "chat.cmd.stop", "", "session"),
            new("b", "chat.cmd.new", "", "session"),
        };

        var filtered = ChatCommandPalette.Filter(items, LabelOf, "parar");

        filtered.First().Id.ShouldBe("a");
    }

    [Fact]
    public void Dado_QueryKeyword_Quando_Filter_Entao_CasaPorKeyword()
    {
        var items = ChatCommands.Build(new ChatCommandState(true, false, false, false));

        // "conversa" é keyword de chat.new ("new session conversa nova").
        var filtered = ChatCommandPalette.Filter(items, LabelOf, "conversa");

        filtered.ShouldContain(i => i.Id == "chat.new");
    }

    [Fact]
    public void Dado_QuerySemMatch_Quando_Filter_Entao_Vazio()
    {
        var items = ChatCommands.Build(new ChatCommandState(true, false, false, false));

        ChatCommandPalette.Filter(items, LabelOf, "xyznada").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_MultiplasPalavras_Quando_Filter_Entao_TodasDevemCasar()
    {
        var items = ChatCommands.Build(new ChatCommandState(true, false, false, false));

        // "abrir terminal" casa label da aba terminal; "abrir xyz" não casa nada.
        ChatCommandPalette.Filter(items, LabelOf, "abrir terminal")
            .ShouldContain(i => i.Id == "ws.tab.terminal");
        ChatCommandPalette.Filter(items, LabelOf, "abrir xyz").ShouldBeEmpty();
    }

    // ------------------------------------------------------------------
    // RF-001: ParseSuggestions.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("- rodar testes\n- abrir PR\n- revisar diff", 3)]
    [InlineData("1. rodar testes\n2. abrir PR\n3. revisar diff\n4. extra", 3)]
    [InlineData("rodar testes", 1)]
    [InlineData("", 0)]
    [InlineData("\n\n  \n", 0)]
    public void Dado_TextoBruto_Quando_ParseSuggestions_Entao_LinhasLimpasNoMax3(
        string raw, int esperado)
    {
        var suggestions = ChatService.ParseSuggestions(raw);

        suggestions.Count.ShouldBe(esperado);
        suggestions.ShouldAllBe(s => !s.StartsWith('-') && !s.StartsWith('*') && s.Length <= 120);
    }

    [Fact]
    public void Dado_LinhasDuplicadas_Quando_ParseSuggestions_Entao_Distinct()
    {
        var suggestions = ChatService.ParseSuggestions("rodar testes\nrodar testes\nabrir pr");

        suggestions.Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------
    // RF-001/RF-007: SuggestNextActionsAsync gates + degrade.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dado_ConversaInexistente_Quando_Suggest_Entao_Null()
    {
        var service = NewService(new FailingHandler());

        var result = await service.SuggestNextActionsAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunAtiva_Quando_Suggest_Entao_VazioSemChamarProvider()
    {
        var conversation = await NewConversationAsync();
        SeedAssistant(conversation, "terminei a tarefa");
        var service = NewService(new FailingHandler());
        var cts = await _coordinator.BeginAsync(conversation.Id.Value);

        var result = await service.SuggestNextActionsAsync(conversation.Id.Value, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
        _coordinator.End(conversation.Id.Value, cts);
    }

    [Fact]
    public async Task Dado_SemMensagemAssistant_Quando_Suggest_Entao_Vazio()
    {
        var conversation = await NewConversationAsync();
        var service = NewService(new FailingHandler());

        var result = await service.SuggestNextActionsAsync(conversation.Id.Value, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ProviderFalha_Quando_Suggest_Entao_VazioSemExcecao()
    {
        var conversation = await NewConversationAsync();
        SeedAssistant(conversation, "implementei o endpoint");
        var service = NewService(new FailingHandler());

        var result = await service.SuggestNextActionsAsync(conversation.Id.Value, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RespostaProvider_Quando_Suggest_Entao_Ate3Chips()
    {
        var conversation = await NewConversationAsync();
        SeedAssistant(conversation, "endpoint criado e testes passando");
        var service = NewService(new SseHandler("- rodar a suite completa\n- abrir o PR\n- revisar cobertura"));

        var result = await service.SuggestNextActionsAsync(conversation.Id.Value, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ShouldBe(["rodar a suite completa", "abrir o PR", "revisar cobertura"]);
    }

    [Fact]
    public async Task Dado_TodosAbertos_Quando_Suggest_Entao_PromptLevaTodos()
    {
        var conversation = await NewConversationAsync();
        SeedAssistant(conversation, "parcialmente feito");
        var todoStore = new ChatTodoStore(_dataDir);
        todoStore.Write(conversation.Id.Value,
        [
            new ChatTodoItem("1", "terminar handler", "pending"),
            new ChatTodoItem("2", "doc pronta", "completed"),
        ]);

        string? requestBody = null;
        var service = NewService(new SseHandler("continuar handler", b => requestBody = b), todoStore);

        await service.SuggestNextActionsAsync(conversation.Id.Value, CancellationToken.None);

        requestBody.ShouldNotBeNull();
        requestBody.ShouldContain("terminar handler");
        requestBody.ShouldNotContain("doc pronta");
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
