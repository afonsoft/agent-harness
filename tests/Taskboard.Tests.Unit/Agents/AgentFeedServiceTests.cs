using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications P1: produce + dedup merge (RF-002/RF-008),
/// cursor paging (RF-003), unread model (RF-004) and badge counts (RF-005).
/// </summary>
public class AgentFeedServiceTests
{
    private static (AgentFeedService Svc, IAgentFeedRepository Items, IUserFeedReadRepository Reads) NewSut(
        IAgentFeedNotifier? notifier = null)
    {
        var items = Substitute.For<IAgentFeedRepository>();
        var reads = Substitute.For<IUserFeedReadRepository>();
        return (new AgentFeedService(items, reads, notifier), items, reads);
    }

    private static FeedItemRequest Request(
        AgentFeedKind kind = AgentFeedKind.Attention,
        string sessionKey = "task:1",
        string deepLink = "/agents?tab=dashboard&focus=1",
        bool needsAttention = true) =>
        new(kind, "title", "body", deepLink, sessionKey, "claude",
            null, needsAttention, "local");

    private static AgentFeedItemDto Item(string id, string title = "t") =>
        new(id, AgentFeedKind.Attention, title, null, "/agents", "task:1",
            "claude", null, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            true, 0, true);

    // ---------- RF-002: attention transition → feed item ----------

    [Fact]
    public void Dado_SinalWaitingYouMailbox_Quando_FromAttentionSignal_Entao_Question()
    {
        var signal = new AttentionSignal(
            "task:1", "claude", AttentionSignalKind.Mailbox,
            AgentAttentionState.WaitingYou, "ask", AtUtc: DateTime.UtcNow);

        var item = AgentFeedService.FromAttentionSignal(signal, AgentAttentionState.WaitingYou);

        item.ShouldNotBeNull();
        item.Kind.ShouldBe(AgentFeedKind.Question);
        item.NeedsAttention.ShouldBeTrue();
        item.DeepLink.ShouldBe("/agents?tab=dashboard&focus=1");
    }

    [Fact]
    public void Dado_SinalWaitingYouNaoMailbox_Quando_FromAttentionSignal_Entao_Attention()
    {
        var signal = new AttentionSignal(
            "term:9", "claude", AttentionSignalKind.Hook,
            AgentAttentionState.WaitingYou, "perm", AtUtc: DateTime.UtcNow);

        var item = AgentFeedService.FromAttentionSignal(signal, AgentAttentionState.WaitingYou);

        item.ShouldNotBeNull();
        item.Kind.ShouldBe(AgentFeedKind.Attention);
        item.DeepLink.ShouldBe("/terminal");
    }

    [Fact]
    public void Dado_SinalBlockedEDone_Quando_FromAttentionSignal_Entao_FailureECompletion()
    {
        var blocked = AgentFeedService.FromAttentionSignal(
            new AttentionSignal("task:1", "claude", AttentionSignalKind.Lifecycle,
                AgentAttentionState.Blocked, AtUtc: DateTime.UtcNow),
            AgentAttentionState.Blocked);
        var done = AgentFeedService.FromAttentionSignal(
            new AttentionSignal("task:1", "claude", AttentionSignalKind.Lifecycle,
                AgentAttentionState.Blocked, AtUtc: DateTime.UtcNow),
            AgentAttentionState.Done);

        blocked!.Kind.ShouldBe(AgentFeedKind.Failure);
        blocked.NeedsAttention.ShouldBeTrue();
        done!.Kind.ShouldBe(AgentFeedKind.Completion);
        done.NeedsAttention.ShouldBeFalse();
    }

    [Fact]
    public void Dado_SinalWorking_Quando_FromAttentionSignal_Entao_Nulo()
    {
        AgentFeedService.FromAttentionSignal(
            new AttentionSignal("task:1", "claude", AttentionSignalKind.Hook,
                AgentAttentionState.Working, AtUtc: DateTime.UtcNow),
            AgentAttentionState.Working).ShouldBeNull();
        AgentFeedService.FromAttentionSignal(
            new AttentionSignal("task:1", "claude", AttentionSignalKind.Hook,
                AgentAttentionState.Working, AtUtc: DateTime.UtcNow),
            AgentAttentionState.Idle).ShouldBeNull();
    }

    [Theory]
    [InlineData("task:abc", "/agents?tab=dashboard&focus=abc")]
    [InlineData("run:r1", "/cockpit")]
    [InlineData("term:t1", "/terminal")]
    [InlineData("conv:c1", "/ai-chat?c=c1")]
    [InlineData("outro", "/agents?tab=dashboard")]
    public void Dado_SessionKey_Quando_DeepLinkFor_Entao_LinkDoSurface(string key, string expected)
    {
        AgentFeedService.DeepLinkFor(key).ShouldBe(expected);
    }

    // ---------- RF-008: dedup merge ----------

    [Fact]
    public async Task Dado_ItemDentroDaJanela_Quando_Produce_Entao_MergeSemNovoItem()
    {
        var (svc, items, _) = NewSut();
        items.FindDedupAsync(
                "local", AgentFeedKind.Attention, "task:1",
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Item("f-1"));
        items.MergeRepeatAsync("f-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Item("f-1"));

        var result = await svc.ProduceAsync(Request());

        result!.Id.ShouldBe("f-1");
        await items.DidNotReceive().AddAsync(
            Arg.Any<FeedItemRequest>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SemItemNaJanela_Quando_Produce_Entao_AddNovoItem()
    {
        var (svc, items, _) = NewSut();
        items.FindDedupAsync(
                Arg.Any<string>(), Arg.Any<AgentFeedKind>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((AgentFeedItemDto?)null);
        items.AddAsync(Arg.Any<FeedItemRequest>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Item("f-9"));

        var result = await svc.ProduceAsync(Request());

        result!.Id.ShouldBe("f-9");
        await items.DidNotReceive().MergeRepeatAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ---------- RF-003: paging ----------

    [Fact]
    public async Task Dado_PaginaCheia_Quando_GetPage_Entao_CursorDoUltimo()
    {
        var (svc, items, _) = NewSut();
        var page = new[] { Item("a"), Item("b") };
        items.ListPageAsync(
                "local", null, null, 2, Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await svc.GetPageAsync("local", limit: 2);

        result.NextCursor.ShouldNotBeNull();
        result.NextCursor.ShouldContain('|');
        result.NextCursor.ShouldContain('b');
    }

    [Fact]
    public async Task Dado_PaginaIncompleta_Quando_GetPage_Entao_SemCursor()
    {
        var (svc, items, _) = NewSut();
        items.ListPageAsync(
                Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Item("a") });

        var result = await svc.GetPageAsync("local", limit: 50);

        result.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_CursorValido_Quando_GetPage_Entao_PassaBeforeAoRepo()
    {
        var (svc, items, _) = NewSut();
        var cursor = "2026-10-20T10:00:00.0000000Z|f-5";
        items.ListPageAsync(
                Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentFeedItemDto>());

        await svc.GetPageAsync("local", cursor);

        await items.Received(1).ListPageAsync(
            "local",
            new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
            "f-5", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CursorInvalido_Quando_GetPage_Entao_SemBefore()
    {
        var (svc, items, _) = NewSut();
        items.ListPageAsync(
                Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentFeedItemDto>());

        await svc.GetPageAsync("local", "cursor-lixo");

        await items.Received(1).ListPageAsync(
            "local", null, null, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------- RF-004/RF-005: unread ----------

    [Fact]
    public async Task Dado_Itens_Quando_UnreadCount_Entao_TotalEAttention()
    {
        var (svc, items, _) = NewSut();
        items.CountUnreadAsync("local", false, Arg.Any<CancellationToken>()).Returns(7);
        items.CountUnreadAsync("local", true, Arg.Any<CancellationToken>()).Returns(3);

        var counts = await svc.GetUnreadCountAsync("local");

        counts.Total.ShouldBe(7);
        counts.Attention.ShouldBe(3);
    }

    [Fact]
    public async Task Dado_Item_Quando_ReadOps_Entao_DelegaAoRepo()
    {
        var (svc, _, reads) = NewSut();

        await svc.MarkReadAsync("u1", "f-1");
        await svc.MarkUnreadAsync("u1", "f-2");
        await svc.MarkAllReadAsync("u1");

        await reads.Received(1).MarkReadAsync(
            "u1", "f-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await reads.Received(1).MarkUnreadAsync(
            "u1", "f-2", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await reads.Received(1).DeleteAllForUserAsync("u1", Arg.Any<CancellationToken>());
    }

    // ---------- RF-010: agent.feed push ----------

    [Fact]
    public async Task Dado_ProduceComItem_Quando_Notifier_Entao_PushItemComCounts()
    {
        var notifier = Substitute.For<IAgentFeedNotifier>();
        var (svc, items, _) = NewSut(notifier);
        items.FindDedupAsync(
                Arg.Any<string>(), Arg.Any<AgentFeedKind>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((AgentFeedItemDto?)null);
        items.AddAsync(Arg.Any<FeedItemRequest>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Item("f-1"));
        items.CountUnreadAsync("local", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(4);

        await svc.ProduceAsync(Request());

        await notifier.Received(1).PushAsync(
            "local",
            Arg.Is<AgentFeedPushDto>(p =>
                p.Kind == "item" && p.Item!.Id == "f-1"
                && p.UnreadCounts.Total == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_MarkOps_Quando_Notifier_Entao_PushPatch()
    {
        var notifier = Substitute.For<IAgentFeedNotifier>();
        var (svc, items, _) = NewSut(notifier);
        items.CountUnreadAsync("u1", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(2);

        await svc.MarkReadAsync("u1", "f-1");
        await svc.MarkUnreadAsync("u1", "f-2");
        await svc.MarkAllReadAsync("u1");

        await notifier.Received(3).PushAsync(
            "u1",
            Arg.Is<AgentFeedPushDto>(p => p.Kind == "patch" && p.Item == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NotifierFalha_Quando_Produce_Entao_ItemGravadoMesmoAssim()
    {
        var notifier = Substitute.For<IAgentFeedNotifier>();
        notifier.PushAsync(Arg.Any<string>(), Arg.Any<AgentFeedPushDto>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("hub down"));
        var (svc, items, _) = NewSut(notifier);
        items.FindDedupAsync(
                Arg.Any<string>(), Arg.Any<AgentFeedKind>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((AgentFeedItemDto?)null);
        items.AddAsync(Arg.Any<FeedItemRequest>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Item("f-2"));

        var result = await svc.ProduceAsync(Request());

        result!.Id.ShouldBe("f-2");
    }
}
