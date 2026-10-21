using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity P3: RF-008 delete guards +
/// SessionFileActions $HOME confinement.
/// </summary>
public class SessionActionServiceTests : IDisposable
{
    private const string User = "local";

    private readonly string _root = Path.Join(
        Path.GetTempPath(), $"session-actions-{Guid.NewGuid():N}");
    private readonly IAgentSessionIndexService _index =
        Substitute.For<IAgentSessionIndexService>();
    private readonly ISessionFileActions _files =
        Substitute.For<ISessionFileActions>();
    private readonly IAgentFeedService _feed =
        Substitute.For<IAgentFeedService>();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SessionActionService NewService() =>
        new(_index, _files, _feed);

    private void StubRow(SessionIndexEntryDto row)
    {
        _index.GetAsync(
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentSessionsIndexDto(
                [new SessionGroupDto(row.Cli, row.Cli, 1, [row])],
                1, [], true)));
    }

    private static SessionIndexEntryDto Row(
        string id, string cli, string? path, bool live = false) =>
        new(cli, id, $"t-{id}", "/tmp/wd", null, null,
            DateTime.UnixEpoch, DateTime.UnixEpoch, null, 0,
            path, $"{cli} --resume {id}", false, live, false, null, null);

    // ---- delete guards ----

    [Fact]
    public async Task Dado_CliDbBacked_Quando_Delete_Entao_NotDeletable()
    {
        var result = await NewService().DeleteAsync(User, "devin", "s1");

        result.ShouldBe(SessionDeleteResult.NotDeletable);
        await _files.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Dado_SessaoInexistente_Quando_Delete_Entao_NotFound()
    {
        _index.GetAsync(
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentSessionsIndexDto([], 0, [], true)));

        var result = await NewService().DeleteAsync(User, "claude", "nope");

        result.ShouldBe(SessionDeleteResult.NotFound);
    }

    [Fact]
    public async Task Dado_SessaoLive_Quando_Delete_Entao_Live()
    {
        StubRow(Row("s1", "claude", "/home/u/.claude/x.jsonl", live: true));

        var result = await NewService().DeleteAsync(User, "claude", "s1");

        result.ShouldBe(SessionDeleteResult.Live);
        await _files.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task Dado_PathForaDoHome_Quando_Delete_Entao_OutsideHome()
    {
        StubRow(Row("s1", "claude", "/etc/passwd"));
        _files.IsUnderHome("/etc/passwd").Returns(false);

        var result = await NewService().DeleteAsync(User, "claude", "s1");

        result.ShouldBe(SessionDeleteResult.OutsideHome);
    }

    [Fact]
    public async Task Dado_SessaoOk_Quando_Delete_Entao_RemoveEEmiteFeedEInvalida()
    {
        var path = Path.Join(_root, ".claude", "projects", "p", "s1.jsonl");
        StubRow(Row("s1", "claude", path));
        _files.IsUnderHome(path).Returns(true);

        var result = await NewService().DeleteAsync(User, "claude", "s1");

        result.ShouldBe(SessionDeleteResult.Deleted);
        await _files.Received(1).DeleteAsync(path, Arg.Any<CancellationToken>());
        _index.Received(1).Invalidate();
        await _feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r =>
                r.Kind == AgentFeedKind.SessionDeleted && r.UserId == User),
            Arg.Any<CancellationToken>());
    }

    // ---- SessionFileActions ----

    [Fact]
    public void Dado_PathNoHome_Quando_IsUnderHome_Entao_True()
    {
        var actions = new SessionFileActions(home: _root);

        actions.IsUnderHome(Path.Join(_root, "a", "b.txt")).ShouldBeTrue();
        actions.IsUnderHome(_root).ShouldBeTrue();
    }

    [Fact]
    public void Dado_PathForaDoHome_Quando_IsUnderHome_Entao_False()
    {
        var actions = new SessionFileActions(home: _root);

        actions.IsUnderHome("/etc/passwd").ShouldBeFalse();
        actions.IsUnderHome(Path.Join(_root, "..", "escape.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_PathForaDoHome_Quando_DeleteAsync_Entao_Recusa()
    {
        var actions = new SessionFileActions(home: _root);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await actions.DeleteAsync("/etc/passwd"));
    }

    [Fact]
    public async Task Dado_ArquivoNoHome_Quando_DeleteAsync_Entao_Remove()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Join(_root, "del.txt");
        File.WriteAllText(path, "x");
        var actions = new SessionFileActions(home: _root);

        await actions.DeleteAsync(path);

        File.Exists(path).ShouldBeFalse();
    }
}
