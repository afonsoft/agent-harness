using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Integrations.Agents;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity P1: index enriquecido (RF-001),
/// grouping/sort (RF-002), chips + hideEmpty (RF-005) e title derivation
/// (RF-006).
/// </summary>
public class AgentSessionIndexServiceTests : IDisposable
{
    private const string User = "local";

    private readonly string _root = Path.Join(
        Path.GetTempPath(), $"session-index-{Guid.NewGuid():N}");
    private readonly ISessionCliVisibilityRepository _visibility =
        Substitute.For<ISessionCliVisibilityRepository>();
    private readonly IRepository<UserPreference> _users =
        Substitute.For<IRepository<UserPreference>>();
    private readonly ServiceProvider _provider;
    private readonly IAgentSessionScanner _scanner =
        Substitute.For<IAgentSessionScanner>();
    private readonly IAgentSessionGitProbe _gitProbe =
        Substitute.For<IAgentSessionGitProbe>();

    public AgentSessionIndexServiceTests()
    {
        _scanner.SupportedClis
            .Returns(["claude", "codex", "opencode", "gemini", "agy", "devin"]);
        _gitProbe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(string?, string?)>((null, null)));
        _visibility.ListHiddenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        _users.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>([]));
        var services = new ServiceCollection();
        services.AddSingleton(_visibility);
        services.AddSingleton(_users);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AgentSessionIndexService NewService(
        IAgentSessionScanner? scanner = null,
        ILiveAgentSessionCatalog? live = null,
        IAgentTranscriptSearcher? searcher = null) =>
        new(_provider, scanner ?? new AgentSessionScanner(_root), _gitProbe,
            live, searcher);

    private string WriteClaudeSession(string id, params string[] lines)
    {
        var dir = Path.Join(_root, ".claude", "projects", "proj-a");
        Directory.CreateDirectory(dir);
        var path = Path.Join(dir, $"{id}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    // ---- RF-006: title derivation ----

    [Fact]
    public async Task Dado_TranscriptComMarkdownEAnsi_Quando_Index_Entao_TituloLimpo()
    {
        var id = Guid.NewGuid().ToString();
        WriteClaudeSession(
            id,
            """{"type":"summary","summary":"x"}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"\u001b[31m**Fix** the `parser` heading\u001b[0m\nsecond line"}]}}""");

        var index = await NewService().GetAsync(User);

        var entry = index.Groups.SelectMany(g => g.Sessions).ShouldHaveSingleItem();
        entry.Title.ShouldBe("Fix the parser heading");
        entry.Title.Length.ShouldBeLessThanOrEqualTo(80);
    }

    [Fact]
    public async Task Dado_TranscriptSemUserMessage_Quando_Index_Entao_TituloFallback()
    {
        var id = Guid.NewGuid().ToString("N");
        WriteClaudeSession(id, """{"type":"summary","summary":"only"}""");

        var index = await NewService().GetAsync(User);

        var entry = index.Groups.SelectMany(g => g.Sessions).ShouldHaveSingleItem();
        entry.Title.ShouldBe($"session-{id[..8]}");
    }

    [Fact]
    public async Task Dado_PrimeiraLinhaLonga_Quando_Index_Entao_TruncaEm80()
    {
        var id = Guid.NewGuid().ToString();
        var longText = new string('a', 200);
        WriteClaudeSession(
            id,
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\""
                + longText + "\"}]}}");

        var index = await NewService().GetAsync(User);

        index.Groups.SelectMany(g => g.Sessions).Single().Title.ShouldBe(longText[..80]);
    }

    // ---- RF-002: grouping + sort ----

    [Fact]
    public async Task Dado_SessoesDeDoisDias_Quando_AgrupaPorDia_Entao_DoisGrupos()
    {
        var a = WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"d1"}}""");
        var b = WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"d2"}}""");
        File.SetLastWriteTimeUtc(a, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(b, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));

        var index = await NewService().GetAsync(User, group: "day");

        index.Groups.Where(g => g.Count > 0).Select(g => g.Key)
            .ShouldBe(["2026-10-21", "2026-10-20"], ignoreOrder: true);
    }

    [Fact]
    public async Task Dado_Sessoes_Quando_SortTitulo_Entao_OrdenaAlfabetico()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"zeta"}}""");
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"alpha"}}""");

        var index = await NewService().GetAsync(User, sort: "title");

        var titles = index.Groups.Single(g => g.Count == 2)
            .Sessions.Select(s => s.Title).ToList();
        titles.ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public void Dado_LiveNoMeio_Quando_Sort_Entao_LiveNoTopo()
    {
        var rows = new List<SessionIndexEntryDto>
        {
            Entry("a", modified: 3, live: false),
            Entry("b", modified: 1, live: true),
            Entry("c", modified: 2, live: false),
        };

        var sorted = AgentSessionIndexService.SortRows(rows, "modified");

        sorted[0].SessionId.ShouldBe("b");
        sorted.Skip(1).Select(s => s.SessionId).ShouldBe(["a", "c"]);
    }

    // ---- RF-005: chips + visibility ----

    [Fact]
    public async Task Dado_CliEscondido_Quando_Get_Entao_LinhasOcultasMasChipPermanece()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"x"}}""");
        _visibility.ListHiddenAsync(User, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["claude"]));

        var index = await NewService().GetAsync(User);

        index.Total.ShouldBe(0);
        index.Groups.ShouldAllBe(g => g.Count == 0);
        var chip = index.Chips.Single(c => c.Cli == "claude");
        chip.Hidden.ShouldBeTrue();
        chip.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_CliSemSessoes_Quando_HideEmptyOff_Entao_GrupoVazio()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"x"}}""");

        var index = await NewService().GetAsync(User, hideEmpty: false);

        index.HideEmpty.ShouldBeFalse();
        index.Groups.ShouldContain(g => g.Count == 0);
        index.Groups.ShouldContain(g => g.Key == "claude" && g.Count == 1);
    }

    [Fact]
    public async Task Dado_CliSemSessoes_Quando_HideEmptyOn_Entao_SemGrupoVazio()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"x"}}""");

        var index = await NewService().GetAsync(User);

        index.HideEmpty.ShouldBeTrue();
        index.Groups.ShouldAllBe(g => g.Count > 0);
    }

    // ---- RF-002: query filter ----

    [Fact]
    public async Task Dado_DuasSessoes_Quando_QueryPorTitulo_Entao_Filtra()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"fix the flaky test"}}""");
        WriteClaudeSession(Guid.NewGuid().ToString(), """{"type":"user","message":{"content":"update readme"}}""");

        var index = await NewService().GetAsync(User, q: "flaky");

        index.Total.ShouldBe(1);
        index.Groups.SelectMany(g => g.Sessions).Single().Title.ShouldBe("fix the flaky test");
    }

    // ---- RF-001: resume + rebuild TTL ----

    [Fact]
    public async Task Dado_SessaoClaude_Quando_Index_Entao_ResumeCommandPresente()
    {
        var id = Guid.NewGuid().ToString();
        WriteClaudeSession(id, """{"type":"user","message":{"content":"hi"}}""");

        var index = await NewService().GetAsync(User);

        var entry = index.Groups.SelectMany(g => g.Sessions).ShouldHaveSingleItem();
        entry.ResumeCommand.ShouldBe($"claude --resume {id}");
        entry.HasNativeChat.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DoisGetsDentroDe60s_Quando_Index_Entao_ScannerChamadoUmaVez()
    {
        _scanner.ScanAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentSessionInfoDto>>([]));

        var service = NewService(_scanner);
        await service.GetAsync(User);
        await service.GetAsync(User);

        await _scanner.Received(1).ScanAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Invalidate_Quando_Get_Entao_RebuildImediato()
    {
        _scanner.ScanAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentSessionInfoDto>>([]));

        var service = NewService(_scanner);
        await service.GetAsync(User);
        service.Invalidate();
        await service.GetAsync(User);

        await _scanner.Received(2).ScanAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- RF-005: prefs roundtrip ----

    [Fact]
    public async Task Dado_SavePrefs_Quando_EscondeCli_Entao_PersisteNoRepo()
    {
        var service = NewService(_scanner);

        await service.SaveViewPrefsAsync(
            User, new SessionViewPrefsDto(HideEmpty: false, HiddenClis: ["codex"]));

        await _visibility.Received(1).SetHiddenAsync(
            User, "codex", true, Arg.Any<CancellationToken>());
    }

    private static SessionIndexEntryDto Entry(
        string id, int modified, bool live, string cli = "claude") =>
        new(cli, id, $"t-{id}", "/tmp/x", null, null,
            DateTime.UnixEpoch, DateTime.UnixEpoch.AddMinutes(modified),
            null, 0, "/tmp/x.jsonl", "x --resume", false, live, false, null, null);

    // ---- RF-003 (P2): live-join ----

    [Fact]
    public async Task Dado_LiveComNativeId_Quando_Index_Entao_RowLiveEPinned()
    {
        var id = Guid.NewGuid().ToString();
        WriteClaudeSession(id, """{"type":"user","message":{"content":"zzz older title"}}""");
        WriteClaudeSession(Guid.NewGuid().ToString(),
            """{"type":"user","message":{"content":"aaa newer title"}}""");
        var catalog = Substitute.For<ILiveAgentSessionCatalog>();
        catalog.Snapshot().Returns([new LiveAgentSessionDto("issue:x", "claude", null, id)]);

        var index = await NewService(live: catalog).GetAsync(User, sort: "title");

        var rows = index.Groups.SelectMany(g => g.Sessions).ToList();
        rows[0].SessionId.ShouldBe(id);
        rows[0].Live.ShouldBeTrue();
        rows[1].Live.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_LiveComCliECwd_Quando_Index_Entao_RowLive()
    {
        var id = Guid.NewGuid().ToString();
        WriteClaudeSession(id, """{"type":"user","message":{"content":"hello"}}""");
        var catalog = Substitute.For<ILiveAgentSessionCatalog>();
        // scanner decodes the "proj-a" slug back to cwd "/proj/a".
        catalog.Snapshot().Returns([
            new LiveAgentSessionDto("term:t1", "claude", "/proj/a")]);

        var index = await NewService(live: catalog).GetAsync(User);

        index.Groups.SelectMany(g => g.Sessions).Single().Live.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_SemLive_Quando_Index_Entao_NadaMarcado()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(),
            """{"type":"user","message":{"content":"hello"}}""");

        var index = await NewService().GetAsync(User);

        index.Groups.SelectMany(g => g.Sessions).Single().Live.ShouldBeFalse();
    }

    // ---- RF-002 (P2): transcript search ----

    [Fact]
    public async Task Dado_QuerySoNoTranscript_Quando_TranscriptTrue_Entao_LinhaEntraComSnippet()
    {
        var path = WriteClaudeSession(Guid.NewGuid().ToString(),
            """{"type":"user","message":{"content":"update readme"}}""");
        var searcher = Substitute.For<IAgentTranscriptSearcher>();
        searcher.SearchAsync(
                Arg.Any<IReadOnlyList<string>>(), "flaky",
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { [path] = "found flaky marker" }));

        var index = await NewService(searcher: searcher)
            .GetAsync(User, q: "flaky", transcript: true);

        var row = index.Groups.SelectMany(g => g.Sessions).ShouldHaveSingleItem();
        row.MatchSnippet.ShouldBe("found flaky marker");
    }

    [Fact]
    public async Task Dado_QuerySoNoTranscript_Quando_TranscriptFalse_Entao_SemLinha()
    {
        WriteClaudeSession(Guid.NewGuid().ToString(),
            """{"type":"user","message":{"content":"update readme"}}""");
        var searcher = Substitute.For<IAgentTranscriptSearcher>();

        var index = await NewService(searcher: searcher)
            .GetAsync(User, q: "flaky", transcript: false);

        index.Total.ShouldBe(0);
        await searcher.DidNotReceiveWithAnyArgs()
            .SearchAsync(default!, default!, default);
    }

    [Fact]
    public async Task Dado_QueryNoTituloENoTranscript_Quando_TranscriptTrue_Entao_SnippetAnexa()
    {
        var path = WriteClaudeSession(Guid.NewGuid().ToString(),
            """{"type":"user","message":{"content":"fix the flaky test"}}""");
        var searcher = Substitute.For<IAgentTranscriptSearcher>();
        searcher.SearchAsync(
                Arg.Any<IReadOnlyList<string>>(), "flaky",
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { [path] = "ctx: flaky" }));

        var index = await NewService(searcher: searcher)
            .GetAsync(User, q: "flaky", transcript: true);

        var row = index.Groups.SelectMany(g => g.Sessions).ShouldHaveSingleItem();
        row.MatchSnippet.ShouldBe("ctx: flaky");
    }
}
