using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Tools:Enabled"] = "true",
                ["Taskboard:Chat:MaxToolIterations"] = "4",
            })
            .Build();

        var client = new OpenAiCompatibleClient(new HttpClient(new FakeProviderHandler()));
        var tools = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["echo_tool"] = new FakeEchoTool(),
        };
        _service = new ChatService(
            new EfCoreRepository<ChatProvider>(_context),
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatMessage>(_context),
            client,
            tools,
            new FakeWorkspaceResolver(),
            configuration);
    }

    public void Dispose()
    {
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

        var events = new List<ChatStreamEvent>();
        await foreach (var chatEvent in await _service.SendMessageAsync(conversation.Id, "rode ls", CancellationToken.None))
        {
            events.Add(chatEvent);
        }

        // 1º turno: tool call + tool result; 2º: resposta final.
        events.OfType<ChatToolCallEvent>().Select(e => e.Name).ShouldBe(["echo_tool"]);
        events.OfType<ChatToolResultEvent>().Single().ResultJson.ShouldContain("echo:rode");
        events.OfType<ChatDeltaEvent>().Select(e => e.Content).ShouldBe(["pronto"]);
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
        await foreach (var _ in await _service.SendMessageAsync(conversation.Id, "termo-unico-xyz", CancellationToken.None))
        {
        }

        var list = await _service.ListConversationsAsync("termo-unico-xyz");

        list.ShouldContain(c => c.Id == conversation.Id);
    }

    [Fact]
    public async Task Dado_ProviderInexistente_Quando_Enviar_Entao_ChatValidationException()
    {
        var conversation = await _service.CreateConversationAsync(
            new CreateChatConversationRequest(_provider.Id, "m1"));
        await _service.DeleteProviderAsync(_provider.Id);

        await Should.ThrowAsync<ChatValidationException>(
            async () => await _service.SendMessageAsync(conversation.Id, "oi", CancellationToken.None));
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

    private sealed class FakeWorkspaceResolver : IWorkspacePathResolver
    {
        public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
        {
            exists = false;
            return Path.GetTempPath();
        }
    }

    /// <summary>Provider fake: 1ª chamada devolve tool_calls, 2ª devolve a resposta final.</summary>
    private sealed class FakeProviderHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var body = call == 1
                ? Sse("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"echo_tool","arguments":"{\"text\":\"rode\"}"}}]}}]}""")
                : Sse("""data: {"choices":[{"delta":{"content":"pronto"}}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            });
        }

        private static string Sse(string line) => $"{line}\ndata: [DONE]\n";
    }
}
