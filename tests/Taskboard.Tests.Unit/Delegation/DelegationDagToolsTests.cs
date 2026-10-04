using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261005-delegation-dag-mailbox: tools de delegação do chat —
/// delegate_task/fanout/status/compare + agent_send/inbox.
/// </summary>
public class DelegationDagToolsTests
{
    private static ChatToolContext Ctx(string? defaultCli = "codex", int depth = 0) =>
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
            DefaultAgentCli: defaultCli);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static IServiceScopeFactory ScopeWith(Action<ServiceCollection> register) =>
        register switch
        {
            { } r => Build(r),
        };

    private static IServiceScopeFactory Build(Action<ServiceCollection> register)
    {
        var collection = new ServiceCollection();
        register(collection);
        return collection.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static DelegationTaskDto TaskDto(
        string id = "task-1", string cli = "codex",
        DelegationTaskStatus status = DelegationTaskStatus.Pending,
        string? groupId = null, string? worktreeRunId = null) =>
        new(id, "conv-1", "prompt", cli, [], null, groupId, worktreeRunId is not null,
            worktreeRunId, "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null);

    // ---- delegate_task ----

    [Fact]
    public async Task Dado_Prompt_Quando_DelegateTask_Entao_CriaTaskNoEscopo()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto());
        var tool = new DelegateTaskTool(
            ScopeWith(c => c.AddSingleton(service)), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"fix bug"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("task-1");
        await service.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.CliName == "codex" && r.Scope == "conv-1" && r.Prompt == "fix bug"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SemCli_Quando_DelegateTask_Entao_Recusa()
    {
        var tool = new DelegateTaskTool(
            ScopeWith(c => c.AddSingleton(Substitute.For<IDelegationService>())),
            Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"x"}"""), Ctx(defaultCli: null), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("no cli");
    }

    [Fact]
    public async Task Dado_DepDesconhecida_Quando_DelegateTask_Entao_Recusa()
    {
        var service = Substitute.For<IDelegationService>();
        service.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns<DelegationTaskDto>(_ =>
                throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "unknown dependency: task-x"));
        var tool = new DelegateTaskTool(
            ScopeWith(c => c.AddSingleton(service)), Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"x","depends_on":["task-x"]}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("unknown dependency");
    }

    [Fact]
    public async Task Dado_DepthSubAgent_Quando_DelegateTask_Entao_RecusaRecursao()
    {
        var tool = new DelegateTaskTool(
            ScopeWith(c => c.AddSingleton(Substitute.For<IDelegationService>())),
            Substitute.For<IGitCommandRunner>());

        var result = await tool.ExecuteAsync(Args("""{"prompt":"x"}"""), Ctx(depth: 1), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- delegate_fanout ----

    [Fact]
    public async Task Dado_FanoutWorktree_Quando_Executa_Entao_CriaPernasNoGrupo()
    {
        var service = Substitute.For<IDelegationService>();
        var created = new List<CreateDelegationTaskRequest>();
        service.CreateTaskAsync(Arg.Do<CreateDelegationTaskRequest>(r => created.Add(r)),
                Arg.Any<CancellationToken>())
            .Returns(ci => TaskDto($"task-{created.Count}", ((CreateDelegationTaskRequest)ci[0]).CliName,
                groupId: ((CreateDelegationTaskRequest)ci[0]).FanoutGroupId, worktreeRunId: null));

        var isolation = Substitute.For<IWorkspaceIsolationService>();
        isolation.CreateWorktreeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => new WorktreeSessionDto(
                "wt", (string)ci[0], $"/wt/{ci[0]}", "b", "active",
                "/repo", "main", null, false, DateTime.UtcNow, DateTime.UtcNow, 0));

        var git = Substitute.For<IGitCommandRunner>();
        git.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "/repo\n", "", false));

        var tool = new DelegateFanoutTool(
            ScopeWith(c =>
            {
                c.AddSingleton(service);
                c.AddSingleton(isolation);
            }), git);

        var result = await tool.ExecuteAsync(
            Args("""{"prompt":"impl X","clis":["codex","claude"],"repository_path":"/repo"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("fan-");
        created.Count.ShouldBe(2);
        created[0].FanoutGroupId.ShouldBe(created[1].FanoutGroupId);
        await isolation.Received(2).CreateWorktreeAsync(
            Arg.Any<string>(), "/repo", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ---- delegate_status ----

    [Fact]
    public async Task Dado_Tasks_Quando_StatusPorEstado_Entao_Filtra()
    {
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                TaskDto("task-1", status: DelegationTaskStatus.Done),
                TaskDto("task-2", status: DelegationTaskStatus.Running),
            ]);
        var tool = new DelegateStatusTool(ScopeWith(c => c.AddSingleton(service)));

        var result = await tool.ExecuteAsync(Args("""{"status":"done"}"""), Ctx(), CancellationToken.None);

        result.Json.ShouldContain("task-1");
        result.Json.ShouldNotContain("task-2");
    }

    // ---- delegate_compare ----

    [Fact]
    public async Task Dado_PernaSemWorktree_Quando_Compare_Entao_NotaSemWorktree()
    {
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", groupId: "fan-1")]);
        var tool = new DelegateCompareTool(ScopeWith(c => c.AddSingleton(service)));

        var result = await tool.ExecuteAsync(Args("""{"group_id":"fan-1"}"""), Ctx(), CancellationToken.None);

        result.Json.ShouldContain("no worktree");
    }

    // ---- agent_send / agent_inbox ----

    [Fact]
    public async Task Dado_Msg_Quando_AgentSend_Entao_PostaNoEscopo()
    {
        var service = Substitute.For<IDelegationService>();
        service.PostAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MailboxMessageDto(
                "msg-1", "conv-1", "codex", "@all",
                ((PostMailboxMessageRequest)ci[0]).Kind,
                ((PostMailboxMessageRequest)ci[0]).Payload, DateTime.UtcNow, null));
        var tool = new AgentSendTool(ScopeWith(c => c.AddSingleton(service)));

        var result = await tool.ExecuteAsync(
            Args("""{"to":"@all","text":"hello workers"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await service.Received(1).PostAsync(
            Arg.Is<PostMailboxMessageRequest>(m =>
                m.Scope == "conv-1" && m.FromAgent == "codex"
                && m.ToAgent == "@all" && m.Kind == "text"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_KindSistema_Quando_AgentSend_Entao_Recusa()
    {
        var tool = new AgentSendTool(ScopeWith(c => c.AddSingleton(Substitute.For<IDelegationService>())));

        var result = await tool.ExecuteAsync(
            Args("""{"to":"@all","text":"x","kind":"worker_done"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("system-only");
    }

    [Fact]
    public async Task Dado_MsgNaCaixa_Quando_AgentInbox_Entao_Lista()
    {
        var service = Substitute.For<IDelegationService>();
        service.ListTasksAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        service.ReadInboxAsync("conv-1", Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(), true, true, Arg.Any<CancellationToken>())
            .Returns([new MailboxMessageDto(
                "msg-1", "conv-1", "codex", "@all", "worker_done",
                "done", DateTime.UtcNow, DateTime.UtcNow)]);
        var tool = new AgentInboxTool(ScopeWith(c => c.AddSingleton(service)));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Json.ShouldContain("msg-1");
        result.Json.ShouldContain("worker_done");
    }
}
