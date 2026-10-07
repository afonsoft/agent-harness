using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Repositories;
using Taskboard.Server.Chat;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261016-chat-browser-tool: `browser_use` — validação de args/URL,
/// recusas antes de abrir scope, persistência do shot como attachment
/// `browser-shot-*` + `AttachmentIds` no resultado, LRU do pool e cap de
/// 25 shots do <see cref="ChatShotStore"/>.
/// </summary>
public sealed class BrowserUseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dataDir;
    private readonly TaskboardDbContext _context;
    private readonly ChatConversation _conversation;
    private readonly ChatAttachmentStore _byteStore;
    private readonly ChatShotStore _shotStore;

    public BrowserUseTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-browser-{Guid.NewGuid()}.sqlite");
        _dataDir = Path.Join(Path.GetTempPath(), $"tb-browser-{Guid.NewGuid()}");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        var provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(provider);
        _conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), provider.Id, "fake", "m1", "browser test");
        _context.ChatConversations.Add(_conversation);
        _context.SaveChanges();
        _byteStore = new ChatAttachmentStore(_dataDir);
        _shotStore = new ChatShotStore(new EfCoreRepository<ChatAttachment>(_context), _byteStore);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static ChatToolContext Ctx(string? conversationId = null) => new(
        WorkspacePath: "/tmp/ws",
        ProviderId: Guid.NewGuid(),
        ProviderBaseUrl: "http://p.test",
        ProviderApiKey: "sk-test",
        ImageModel: string.Empty,
        SearchBackend: string.Empty,
        SearchUrl: string.Empty,
        SearchApiKey: string.Empty,
        ConversationId: conversationId);

    private static JsonElement Args(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    private static ServiceProvider ProviderWith(IBrowserSessionPool pool, IChatShotStore shots) =>
        new ServiceCollection()
            .AddSingleton(pool)
            .AddSingleton(shots)
            .BuildServiceProvider();

    // ------------------------------------------------------------------
    // RF-006: URL gate — http(s) only.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://example.com/x?q=1", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Dado_Url_Quando_UrlAllowed_Entao_SoHttpSPassa(string? url, bool esperado) =>
        BrowserUseTool.UrlAllowed(url).ShouldBe(esperado);

    // ------------------------------------------------------------------
    // Refusals — sem scope de DI (o pool nunca é resolvido).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dado_SemConversa_Quando_ExecuteAsync_Entao_RecusaNoConversation()
    {
        var tool = new BrowserUseTool(Substitute.For<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(Args("""{"action":"screenshot"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("error").GetString().ShouldBe("no-conversation");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"action":"hack"}""")]
    [InlineData("""{"action":42}""")]
    public async Task Dado_ActionInvalida_Quando_ExecuteAsync_Entao_RecusaBadAction(string json)
    {
        var tool = new BrowserUseTool(Substitute.For<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(Args(json), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("error").GetString().ShouldBe("bad-action");
    }

    [Theory]
    [InlineData("""{"action":"navigate"}""")]
    [InlineData("""{"action":"navigate","url":"file:///etc/passwd"}""")]
    [InlineData("""{"action":"navigate","url":"javascript:x()"}""")]
    public async Task Dado_NavigateUrlInvalida_Quando_ExecuteAsync_Entao_RecusaBadUrl(string json)
    {
        var tool = new BrowserUseTool(Substitute.For<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(Args(json), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("error").GetString().ShouldBe("bad-url");
    }

    [Theory]
    [InlineData("""{"action":"click"}""")]
    [InlineData("""{"action":"type","selector":"  "}""")]
    public async Task Dado_SemSelector_Quando_ClickOuType_Entao_RecusaMissingSelector(string json)
    {
        var tool = new BrowserUseTool(Substitute.For<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(Args(json), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("error").GetString().ShouldBe("missing-selector");
    }

    [Fact]
    public async Task Dado_EvalSemScript_Quando_ExecuteAsync_Entao_RecusaMissingScript()
    {
        var tool = new BrowserUseTool(Substitute.For<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"eval_js"}"""), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("error").GetString().ShouldBe("missing-script");
    }

    // ------------------------------------------------------------------
    // Caminho feliz: pool stub retorna shot → attachment + AttachmentIds.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dado_PoolRetornaShot_Quando_Screenshot_Entao_AttachmentIdsERowPersistida()
    {
        var png = new byte[] { 1, 2, 3, 4 };
        var pool = Substitute.For<IBrowserSessionPool>();
        pool.ExecuteAsync(_conversation.Id.Value, Arg.Any<BrowserAction>(), Arg.Any<CancellationToken>())
            .Returns(new BrowserActionResult(
                Ok: true, Url: "http://localhost:5173", Title: "App", Text: null, ShotPng: png, Error: null));
        var services = ProviderWith(pool, _shotStore);
        var tool = new BrowserUseTool(services.GetRequiredService<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"screenshot","fullPage":true}"""),
            Ctx(_conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.AttachmentIds.ShouldNotBeNull();
        result.AttachmentIds.Count.ShouldBe(1);

        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("shotUrl").GetString()!.ShouldContain("/attachments/");
        doc.RootElement.GetProperty("title").GetString().ShouldBe("App");

        var rows = _context.ChatAttachments.ToList();
        rows.Count.ShouldBe(1);
        rows[0].FileName.ShouldStartWith("browser-shot-");
        rows[0].FileName.ShouldEndWith("-screenshot.png");
        rows[0].ContentType.ShouldBe("image/png");
        File.Exists(Path.Join(_byteStore.Root, rows[0].StoragePath)).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_PoolFalha_Quando_ExecuteAsync_Entao_OkFalseSemAttachment()
    {
        var pool = Substitute.For<IBrowserSessionPool>();
        pool.ExecuteAsync(Arg.Any<string>(), Arg.Any<BrowserAction>(), Arg.Any<CancellationToken>())
            .Returns(new BrowserActionResult(
                Ok: false, Url: null, Title: null, Text: null, ShotPng: null, Error: "timeout"));
        var services = ProviderWith(pool, _shotStore);
        var tool = new BrowserUseTool(services.GetRequiredService<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"navigate","url":"http://localhost:1"}"""),
            Ctx(_conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.AttachmentIds.ShouldBeNull();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("error").GetString().ShouldBe("timeout");
        _context.ChatAttachments.ToList().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_AcaoSemShot_Quando_ExtractText_Entao_OkSemAttachment()
    {
        var pool = Substitute.For<IBrowserSessionPool>();
        pool.ExecuteAsync(Arg.Any<string>(), Arg.Any<BrowserAction>(), Arg.Any<CancellationToken>())
            .Returns(new BrowserActionResult(
                Ok: true, Url: "http://x", Title: "T", Text: "conteúdo", ShotPng: null, Error: null));
        var services = ProviderWith(pool, _shotStore);
        var tool = new BrowserUseTool(services.GetRequiredService<IServiceScopeFactory>());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"extract_text"}"""), Ctx(_conversation.Id.Value), CancellationToken.None);

        result.AttachmentIds.ShouldBeNull();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("text").GetString().ShouldBe("conteúdo");
    }

    [Fact]
    public async Task Dado_WaitMsExagerado_Quando_ExecuteAsync_Entao_ClampEm5000()
    {
        BrowserAction? capturado = null;
        var pool = Substitute.For<IBrowserSessionPool>();
        pool.ExecuteAsync(Arg.Any<string>(), Arg.Any<BrowserAction>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturado = ci.Arg<BrowserAction>();
                return new BrowserActionResult(true, null, null, null, null, null);
            });
        var services = ProviderWith(pool, _shotStore);
        var tool = new BrowserUseTool(services.GetRequiredService<IServiceScopeFactory>());

        await tool.ExecuteAsync(
            Args("""{"action":"screenshot","waitMs":99999}"""),
            Ctx(_conversation.Id.Value), CancellationToken.None);

        capturado.ShouldNotBeNull();
        capturado.WaitMs.ShouldBe(5000);
    }

    // ------------------------------------------------------------------
    // RF-007: metadados do tool.
    // ------------------------------------------------------------------

    [Fact]
    public void Dado_BrowserUseTool_Quando_Metadados_Entao_RequireConfirmacaoECapability() =>
        new BrowserUseTool(Substitute.For<IServiceScopeFactory>()).ShouldSatisfyAllConditions(
            t => t.RequiresConfirmation.ShouldBeTrue(),
            t => t.Name.ShouldBe("browser_use"),
            t => t.CapabilityId.ShouldBe("tool:browser_use"));

    // ------------------------------------------------------------------
    // RF-007 risco: eval_js High, demais Medium.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("""{"action":"eval_js","script":"x()"}""", ChatToolRisk.High)]
    [InlineData("""{"action":"navigate","url":"http://x"}""", ChatToolRisk.Medium)]
    [InlineData("""{"action":"click","selector":"#a"}""", ChatToolRisk.Medium)]
    public void Dado_BrowserAction_Quando_Classifica_Entao_RiscoPorAction(string args, ChatToolRisk esperado)
    {
        var verdict = StaticChatToolRiskClassifier.Instance.Classify(
            "browser_use", Args(args), Ctx());
        verdict.Risk.ShouldBe(esperado);
    }

    // ------------------------------------------------------------------
    // RF-005: LRU do pool — os mais antigos são as vítimas.
    // ------------------------------------------------------------------

    [Fact]
    public void Dado_SessionsAcimaDoMax_Quando_Victims_Entao_EvitaMaisAntigas()
    {
        var now = DateTime.UtcNow;
        var entries = new[]
        {
            ("a", now.AddMinutes(-30)),
            ("b", now.AddMinutes(-5)),
            ("c", now.AddMinutes(-60)),
            ("d", now),
        };

        var victims = BrowserSessionLru.Victims(entries, max: 2).ToList();

        victims.ShouldBe(["c", "a"]);
    }

    [Fact]
    public void Dado_SessionsDentroDoMax_Quando_Victims_Entao_NenhumaVitima()
    {
        var now = DateTime.UtcNow;
        var victims = BrowserSessionLru.Victims(
            [("a", now), ("b", now.AddMinutes(-1))], max: 3);

        victims.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------
    // RF-003: ChatShotStore — cap 25, prefixo e listagem.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dado_ShotSalvo_Quando_ListShots_Entao_DtoComPrefixoEDownloadUrl()
    {
        var id = await _shotStore.SaveShotAsync(
            _conversation.Id.Value, "navigate", [9, 9, 9], CancellationToken.None);

        var shots = await _shotStore.ListShotsAsync(_conversation.Id.Value, CancellationToken.None);
        shots.Count.ShouldBe(1);
        shots[0].Id.ShouldBe(id);
        shots[0].FileName.ShouldStartWith("browser-shot-");
        shots[0].DownloadUrl.ShouldBe(
            $"/api/local/chat/conversations/{_conversation.Id.Value}/attachments/{id}/download");
    }

    [Fact]
    public async Task Dado_AcimaDoCap_Quando_SaveShot_Entao_DescartaMaisAntigo()
    {
        var ids = new List<string>();
        for (var i = 0; i < ChatShotStore.MaxShotsPerConversation + 1; i++)
        {
            ids.Add(await _shotStore.SaveShotAsync(
                _conversation.Id.Value, "screenshot", [1], CancellationToken.None));
            // CreatedAt tem resolução de segundo no filename — garantir ordem
            // via tick distinto é desnecessário: EF ordena por CreatedAt (ms).
            await Task.Delay(5);
        }

        var shots = await _shotStore.ListShotsAsync(_conversation.Id.Value, CancellationToken.None);
        shots.Count.ShouldBe(ChatShotStore.MaxShotsPerConversation);
        shots.Select(s => s.Id).ShouldNotContain(ids[0]);

        // O arquivo do shot descartado também sumiu do byte store.
        var orphan = _context.ChatAttachments.IgnoreQueryFilters()
            .Where(a => a.FileName.StartsWith("browser-shot-")).ToList();
        orphan.Count.ShouldBe(ChatShotStore.MaxShotsPerConversation);
    }

    [Fact]
    public async Task Dado_AttachmentsNaoShot_Quando_ListShots_Entao_IgnoraOsDemais()
    {
        // Attachment normal (sem prefixo) não entra na galeria do browser.
        var id = ChatAttachmentId.NewGuid();
        var (path, sha) = _byteStore.Save(id.Value, "text/plain", [1]);
        _context.ChatAttachments.Add(ChatAttachment.Create(
            id, _conversation.Id, "nota.txt", "text/plain", 1, path, sha));
        _context.SaveChanges();

        var shots = await _shotStore.ListShotsAsync(_conversation.Id.Value, CancellationToken.None);
        shots.ShouldBeEmpty();
    }
}
