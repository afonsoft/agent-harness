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
using Taskboard.Server.Chat;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261015-chat-preview-panel: <see cref="ChatPreviewUrl"/> SSRF guard,
/// <see cref="ChatPreviewProxy"/> header/redirect rewriting,
/// <see cref="RegisterPreviewTool"/> e <see cref="ConversationPreviewStore"/>.
/// </summary>
public sealed class ConversationPreviewTests : IDisposable
{
    private readonly string _dbPath = Path.Join(Path.GetTempPath(), $"tb-preview-{Guid.NewGuid()}.sqlite");
    private readonly TaskboardDbContext _context;
    private readonly ChatConversation _conversation;

    public ConversationPreviewTests()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();

        var provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(provider);
        _conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), provider.Id, "fake", "m1", "minha feature");
        _context.ChatConversations.Add(_conversation);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    // ---- ChatPreviewUrl.Normalize (RF-002, RNF SSRF) ----

    [Theory]
    [InlineData("http://localhost:5021/", "/preview/5021/")]
    [InlineData("http://localhost:5021/app/page?q=1", "/preview/5021/app/page?q=1")]
    [InlineData("https://127.0.0.1:3000", "/preview/3000/")]
    [InlineData("http://[::1]:8080/health", "/preview/8080/health")]
    [InlineData("http://LOCALHOST:5021", "/preview/5021/")]
    [InlineData("/preview/5021/app", "/preview/5021/app")]
    [InlineData("/preview/65535/", "/preview/65535/")]
    public void Dado_UrlLoopbackOuPathProxy_Quando_Normalize_Entao_PathPreview(string input, string expected)
        => ChatPreviewUrl.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://localhost:80")]          // porta baixa demais
    [InlineData("http://localhost:1023")]
    [InlineData("http://localhost:65536")]
    [InlineData("http://example.com:5021/")]     // não-loopback
    [InlineData("http://192.168.1.10:5021/")]
    [InlineData("ftp://localhost:5021/")]        // scheme errado
    [InlineData("não é url")]
    [InlineData("/preview/999/")]                // porta abaixo do mínimo
    public void Dado_UrlInvalidaOuNaoLoopback_Quando_Normalize_Entao_Null(string? input)
        => ChatPreviewUrl.Normalize(input).ShouldBeNull();

    [Fact]
    public void Dado_QuoteSemTexto_Quando_FormatBlock_Entao_SoSeletor()
    {
        var quote = new ChatElementQuote("div.card", "div", null, null);
        quote.FormatBlock().ShouldBe("> `div.card`");
    }

    [Fact]
    public void Dado_QuoteComTextoEPagina_Quando_FormatBlock_Entao_BlockquoteCompleto()
    {
        var quote = new ChatElementQuote("h1#t", "h1", "Oi", "http://localhost:5021/");
        quote.FormatBlock().ShouldBe("> `h1#t` \"Oi\" — http://localhost:5021/");
    }

    // ---- ChatPreviewProxy helpers (RF-001/RF-007) ----

    [Theory]
    [InlineData(5021, "app/page", "?a=1", "http://127.0.0.1:5021/app/page?a=1")]
    [InlineData(5021, null, null, "http://127.0.0.1:5021/")]
    public void Dado_PortaValida_Quando_TryCreateTarget_Entao_Loopback(
        int port, string? path, string? query, string expected)
    {
        ChatPreviewProxy.TryCreateTarget(port, path, query, out var target).ShouldBeTrue();
        target.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void Dado_PortaForaDaFaixa_Quando_TryCreateTarget_Entao_False(int port)
        => ChatPreviewProxy.TryCreateTarget(port, null, null, out _).ShouldBeFalse();

    [Theory]
    [InlineData("http://localhost:5021/login", "/preview/5021/login")]
    [InlineData("https://127.0.0.1:8080/a?b=2", "/preview/8080/a?b=2")]
    [InlineData("/relative/path", "/relative/path")]
    [InlineData("http://example.com:5021/x", "http://example.com:5021/x")]
    [InlineData(null, null)]
    public void Dado_Location_Quando_RewriteLocation_Entao_ProxyOuIntacto(string? input, string? expected)
        => ChatPreviewProxy.RewriteLocation(input).ShouldBe(expected);

    [Theory]
    [InlineData("default-src 'self'; frame-ancestors 'none'", "default-src 'self'")]
    [InlineData("frame-ancestors *", null)]
    [InlineData(null, null)]
    [InlineData("default-src 'self'", "default-src 'self'")]
    public void Dado_Csp_Quando_StripFrameAncestors_Entao_RemoveDiretiva(string? input, string? expected)
        => ChatPreviewProxy.StripFrameAncestors(input).ShouldBe(expected);

    // SPEC-20261008-s8949: os WriteAsync de erro devem observar
    // context.RequestAborted — token pré-cancelado aborta a escrita.
    [Fact]
    public async Task Dado_RequestAbortada_Quando_ForwardPortaInvalida_Entao_WriteCancela()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.RequestAborted = new CancellationToken(canceled: true);

        var act = () => ChatPreviewProxy.ForwardAsync(http, new HttpClient(), 80, null);

        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    // ---- RegisterPreviewTool (RF-002) ----

    private RegisterPreviewTool NewTool(IConversationPreviewStore store)
        => new(new ServiceCollection()
            .AddSingleton(store)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>());

    private static ChatToolContext Ctx(string? conversationId) =>
        new("ws", Guid.Empty, "http://p", "k", "m", "s", "u", "k", ConversationId: conversationId);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Dado_SemConversation_Quando_RegisterPreview_Entao_Recusa()
    {
        var store = Substitute.For<IConversationPreviewStore>();
        var result = await NewTool(store).ExecuteAsync(
            Args("""{"url":"http://localhost:5021/"}"""), Ctx(null), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("no-conversation");
    }

    [Fact]
    public async Task Dado_SemUrl_Quando_RegisterPreview_Entao_Recusa()
    {
        var store = Substitute.For<IConversationPreviewStore>();
        var result = await NewTool(store).ExecuteAsync(
            Args("""{}"""), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("missing-url");
    }

    [Fact]
    public async Task Dado_UrlNaoLoopback_Quando_RegisterPreview_Entao_RecusaInvalidUrl()
    {
        var store = Substitute.For<IConversationPreviewStore>();
        var result = await NewTool(store).ExecuteAsync(
            Args("""{"url":"http://evil.example.com:5021/"}"""), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("invalid-url");
        await store.DidNotReceive().SetPreviewUrlAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_UrlLoopback_Quando_RegisterPreview_Entao_NormalizaEPersiste()
    {
        var store = Substitute.For<IConversationPreviewStore>();
        store.SetPreviewUrlAsync("conv-1", "/preview/5021/", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await NewTool(store).ExecuteAsync(
            Args("""{"url":"http://localhost:5021/"}"""), Ctx("conv-1"), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"previewUrl\":\"/preview/5021/\"");
        await store.Received(1).SetPreviewUrlAsync(
            "conv-1", "/preview/5021/", Arg.Any<CancellationToken>());
    }

    // ---- ConversationPreviewStore (RF-002) ----

    [Fact]
    public async Task Dado_ConversaExistente_Quando_SetPreviewUrl_Entao_Persiste()
    {
        var store = new ConversationPreviewStore(new EfCoreRepository<ChatConversation>(_context));

        var ok = await store.SetPreviewUrlAsync(
            _conversation.Id.Value, "/preview/5021/", CancellationToken.None);

        ok.ShouldBeTrue();
        var reloaded = await _context.ChatConversations.FindAsync(_conversation.Id);
        reloaded!.PreviewUrl.ShouldBe("/preview/5021/");
    }

    [Fact]
    public async Task Dado_ConversaInexistente_Quando_SetPreviewUrl_Entao_False()
    {
        var store = new ConversationPreviewStore(new EfCoreRepository<ChatConversation>(_context));

        var ok = await store.SetPreviewUrlAsync(
            ChatConversationId.NewGuid().Value, "/preview/5021/", CancellationToken.None);

        ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_PreviewSetado_Quando_ClearPreview_Entao_Null()
    {
        var store = new ConversationPreviewStore(new EfCoreRepository<ChatConversation>(_context));
        await store.SetPreviewUrlAsync(_conversation.Id.Value, "/preview/5021/", CancellationToken.None);

        await store.SetPreviewUrlAsync(_conversation.Id.Value, null, CancellationToken.None);

        var reloaded = await _context.ChatConversations.FindAsync(_conversation.Id);
        reloaded!.PreviewUrl.ShouldBeNull();
    }
}
