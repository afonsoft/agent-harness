using System.Net;
using System.Text;
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
using Taskboard.Integrations.Chat;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261025 C1 — channel bridge: inbound routing + channel decides.</summary>
public sealed class ChatChannelBridgeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly ChatProvider _provider;
    private readonly ChatService _chat;
    private readonly ChatChannelBridge _bridge;

    public ChatChannelBridgeTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-bridge-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();

        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();

        _chat = NewChatService(_context);
        _bridge = new ChatChannelBridge(
            new EfCoreRepository<ChatChannelBinding>(_context),
            new EfCoreRepository<ChatProvider>(_context),
            _chat,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Channels:Slack:Model"] = "test-model",
            }).Build(),
            NullLogger<ChatChannelBridge>.Instance);
    }

    [Fact]
    public async Task Dado_MensagemPrimeira_Quando_Inbound_Entao_CriaConversaEBinding()
    {
        var conversationId = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.01", "U9", "@afonso", "primeira mensagem"));

        var binding = await _context.ChatChannelBindings.SingleAsync();
        binding.ConversationId.Value.ShouldBe(conversationId);
        binding.ExternalThreadId.ShouldBe("1700.01");

        var detail = await _chat.GetConversationAsync(conversationId, CancellationToken.None);
        detail.ShouldNotBeNull();
        detail!.Conversation.Model.ShouldBe("test-model");
        detail.Messages.ShouldContain(m => m.Role == "user" && m.Content == "primeira mensagem");
    }

    [Fact]
    public async Task Dado_BindingExistente_Quando_InboundNaThread_Entao_RoteiaParaMesmaConversa()
    {
        var first = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.01", "U9", "@afonso", "primeira"));
        var second = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.01", "U9", "@afonso", "segunda"));

        second.ShouldBe(first);
        (await _context.ChatChannelBindings.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_ThreadDiferente_Quando_Inbound_Entao_ConversaNova()
    {
        var first = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.01", "U9", "@afonso", "uma"));
        var second = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.99", "U9", "@afonso", "outra thread"));

        second.ShouldNotBe(first);
        (await _context.ChatChannelBindings.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Dado_AprovacaoCanal_Quando_Decide_Entao_MesmaRowComChannelEAtor()
    {
        var conversationId = await _bridge.InboundMessageAsync(new ChatChannelInboundMessage(
            "slack", "C123", "1700.01", "U9", "@afonso", "setup"));
        var run = await _context.ChatRuns
            .OrderBy(r => r.CreatedAt).LastAsync(r => r.ConversationId == ChatConversationId.From(conversationId));

        var approval = ChatApproval.Create(
            ChatApprovalId.NewGuid(), run.Id, ChatConversationId.From(conversationId),
            "call_1", "shell_exec", """{"command":"rm -rf /tmp/x"}""");
        _context.ChatApprovals.Add(approval);
        await _context.SaveChangesAsync();

        var decided = await _bridge.DecideApprovalAsync(
            approval.Id.Value, "allow", "slack", "@afonso", reason: null, CancellationToken.None);

        decided.ShouldNotBeNull();
        var row = await _context.ChatApprovals.SingleAsync();
        row.Status.Value.ShouldBe("allowed-once");
        row.DecidedBy!.Value.ShouldBe("channel:slack");
        row.Decision!.ShouldContain("slack by @afonso");
    }

    private ChatService NewChatService(TaskboardDbContext context)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Taskboard:Chat:Tools:Enabled"] = "true",
            ["Taskboard:Chat:MaxToolIterations"] = "4",
        }).Build();
        var tools = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["echo_tool"] = new FakeEchoTool(),
        };
        return new ChatService(
            new EfCoreRepository<ChatProvider>(context),
            new EfCoreRepository<ChatConversation>(context),
            new EfCoreRepository<ChatMessage>(context),
            new OpenAiCompatibleClient(new HttpClient(new FakeProviderHandler())),
            new ChatCapabilityRegistry(tools, new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            new FakeWorkspaceResolver(),
            configuration,
            new ChatRunCoordinator(),
            new ServiceCollection().AddHybridCache().Services.BuildServiceProvider()
                .GetRequiredService<HybridCache>(),
            new EfCoreRepository<ChatRun>(context),
            new ChatRunQueue(),
            new ChatRunBroadcaster(),
            new EfCoreRepository<ChatApproval>(context),
            new EfCoreRepository<ChatSteer>(context),
            new ChatApprovalCoordinator(),
            [],
            NullLogger<ChatService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private sealed class FakeEchoTool : IChatTool
    {
        public string Name => "echo_tool";
        public string Description => "echo";
        public string ParametersJson => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        public Task<ChatToolResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult("""{"echo":true}"""));
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

    private sealed class FakeProviderHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"done\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}\ndata: [DONE]\n",
                    Encoding.UTF8, "text/event-stream"),
            });
    }
}
