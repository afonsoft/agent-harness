using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation P1: registry, guard chain, threshold,
/// hibernate → record + feed, resume → respawn + lifecycle signal, pin.
/// </summary>
public class AgentHibernationServiceTests
{
    private const string Key = "term:abc12345";
    // The service stamps with DateTime.UtcNow — test times must track real
    // clock so idle-threshold assertions stay honest.
    private static DateTime Now => DateTime.UtcNow;

    // ---------- fakes ----------

    private sealed class FakeHibernationRepository : IAgentHibernationRepository
    {
        public readonly Dictionary<string, AgentHibernationDto> Rows =
            new(StringComparer.Ordinal);

        public Task<AgentHibernationDto?> GetAsync(string k, CancellationToken ct = default) =>
            Task.FromResult(Rows.GetValueOrDefault(k));

        public Task<IReadOnlyList<AgentHibernationDto>> ListAsync(
            string? userId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentHibernationDto>>(Rows.Values.ToList());

        public Task<HashSet<string>> ListKeysAsync(CancellationToken ct = default) =>
            Task.FromResult(Rows.Values.Where(r => r.IsHibernated)
                .Select(r => r.SessionKey).ToHashSet(StringComparer.Ordinal));

        public Task AddAsync(string k, string u, string cli, string argvJson,
            string? nativeId, string? cwd, DateTime idleAt, DateTime at,
            CancellationToken ct = default)
        {
            var argv = System.Text.Json.JsonSerializer.Deserialize<string[]>(argvJson) ?? [];
            var existing = Rows.GetValueOrDefault(k);
            Rows[k] = new AgentHibernationDto(k, u, cli, true, at, idleAt, argv,
                nativeId, cwd, existing?.AutoResumedCount ?? 0, existing?.Pinned ?? false);
            return Task.CompletedTask;
        }

        public Task PinOnlyAsync(string k, string u, string cli, string? cwd,
            DateTime at, CancellationToken ct = default)
        {
            Rows.TryAdd(k, new AgentHibernationDto(k, u, cli, false, at, at, [],
                null, cwd, 0, true));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string k, CancellationToken ct = default)
        {
            Rows.Remove(k);
            return Task.CompletedTask;
        }

        public Task SetPinnedAsync(string k, bool pinned, CancellationToken ct = default)
        {
            if (Rows.TryGetValue(k, out var r))
            {
                Rows[k] = r with { Pinned = pinned };
            }

            return Task.CompletedTask;
        }

        public Task MarkResumedAsync(string k, bool auto, CancellationToken ct = default)
        {
            if (Rows.TryGetValue(k, out var r))
            {
                Rows[k] = r with
                {
                    IsHibernated = false,
                    AutoResumedCount = r.AutoResumedCount + (auto ? 1 : 0),
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTerminator : ISessionTerminator
    {
        public readonly HashSet<string> Alive = new(StringComparer.Ordinal);
        public readonly List<string> Killed = new();

        public bool IsAlive(string sessionKey) => Alive.Contains(sessionKey);

        public Task<bool> TerminateAsync(string sessionKey, CancellationToken ct = default)
        {
            if (!Alive.Remove(sessionKey))
            {
                return Task.FromResult(false);
            }

            Killed.Add(sessionKey);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeRespawner : ISessionRespawner
    {
        public readonly List<(string Key, string User, IReadOnlyList<string> Argv, string? Cwd)> Calls = new();
        public bool NextResult = true;

        public Task<bool> RespawnAsync(string key, string user,
            IReadOnlyList<string> argv, string? cwd, CancellationToken ct = default)
        {
            Calls.Add((key, user, argv, cwd));
            return Task.FromResult(NextResult);
        }
    }

    private sealed class Harness
    {
        public readonly IAgentSessionStateRepository States =
            Substitute.For<IAgentSessionStateRepository>();
        public readonly FakeHibernationRepository Records = new();
        public readonly IDispatchRepository Dispatches =
            Substitute.For<IDispatchRepository>();
        public readonly IAgentFeedService Feed = Substitute.For<IAgentFeedService>();
        public readonly FakeTerminator Terminator = new();
        public readonly FakeRespawner Respawner = new();
        public readonly IAgentSessionIndexService Index =
            Substitute.For<IAgentSessionIndexService>();
        public readonly IAgentAttentionService Attention =
            Substitute.For<IAgentAttentionService>();
        public readonly AgentHibernationService Service;

        public Harness()
        {
            var services = new ServiceCollection()
                .AddScoped(_ => States)
                .AddScoped<IAgentHibernationRepository>(_ => Records)
                .AddScoped(_ => Dispatches)
                .AddScoped(_ => Feed)
                .AddScoped<IAgentHibernationGuard, ResumeSupportGuard>()
                .AddScoped<IAgentHibernationGuard, AttentionGuard>()
                .AddScoped<IAgentHibernationGuard, PinnedGuard>()
                .AddScoped<IAgentHibernationGuard, OpenDispatchGuard>()
                .BuildServiceProvider();

            Dispatches.ListOpenForSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DispatchDto>());

            Service = new AgentHibernationService(services, Terminator, Respawner,
                Index, Attention, NullLogger<AgentHibernationService>.Instance);
        }

        public AgentSessionStateDto State(
            string key = Key, string cli = "claude",
            AgentAttentionState state = AgentAttentionState.Done,
            int idleMinutes = 45)
        {
            var changed = Now.AddMinutes(-idleMinutes);
            return new AgentSessionStateDto(key, cli, state, null,
                changed, changed, AttentionSignalKind.Lifecycle, null, 1, 0);
        }

        public SessionIndexEntryDto IndexRow(string nativeId, string cli = "claude",
            string cwd = "/repo/w") =>
            new(cli, nativeId, "t", cwd, null, null, Now, Now, null, 0,
                "/x.jsonl", $"{cli} --resume {nativeId}", false, true,
                false, null, null);
    }

    // ---------- registry ----------

    [Fact]
    public void Dado_CliSuportado_Quando_ResumeArgv_Entao_Template()
    {
        AgentCliResumeSupport.ResumeArgv("claude", "s-1")
            .ShouldBe(["claude", "--resume", "s-1"]);
        AgentCliResumeSupport.ResumeArgv("codex", "s-1")
            .ShouldBe(["codex", "resume", "s-1"]);
        AgentCliResumeSupport.ResumeArgv("opencode", "s-1")
            .ShouldBe(["opencode", "--session", "s-1"]);
        AgentCliResumeSupport.ResumeArgv("gemini", "s-1")
            .ShouldBe(["gemini", "--resume", "s-1"]);
    }

    [Fact]
    public void Dado_CliSemResume_Quando_SupportsResume_Entao_Falso()
    {
        AgentCliResumeSupport.SupportsResume("devin").ShouldBeFalse();
        AgentCliResumeSupport.SupportsResume("antigravity").ShouldBeFalse();
        AgentCliResumeSupport.SupportsResume("desconhecido").ShouldBeFalse();
        AgentCliResumeSupport.SupportsResume(null).ShouldBeFalse();
        AgentCliResumeSupport.ResumeArgv("devin", "x").ShouldBeNull();
    }

    // ---------- guards ----------

    [Fact]
    public async Task Dado_CliSemResume_Quando_Hibernate_Entao_Recusado()
    {
        var h = new Harness();
        var state = h.State(cli: "devin");
        h.Terminator.Alive.Add(Key);

        var outcome = await h.Service.HibernateAsync("local", Key);

        outcome.Hibernated.ShouldBeFalse();
        outcome.Reason.ShouldNotBeNull().ShouldContain("resume-support");
        h.Terminator.Killed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_WaitingYou_Quando_Hibernate_Entao_GuardaRecusa()
    {
        var h = new Harness();
        var state = h.State(state: AgentAttentionState.WaitingYou);
        h.States.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(state);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1"));

        var outcome = await h.Service.HibernateAsync("local", Key);

        outcome.Hibernated.ShouldBeFalse();
        outcome.Reason.ShouldNotBeNull().ShouldContain("attention");
        h.Terminator.Killed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_DispatchAberto_Quando_Hibernate_Entao_GuardaRecusa()
    {
        var h = new Harness();
        var state = h.State();
        h.States.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(state);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1"));
        h.Dispatches.ListOpenForSessionAsync(Key, Arg.Any<CancellationToken>())
            .Returns([new DispatchDto("d1", "t1", "r1", "w", 1,
                DispatchState.Dispatched, "{}", Key, null, Now, null, null, null)]);

        var outcome = await h.Service.HibernateAsync("local", Key);

        outcome.Hibernated.ShouldBeFalse();
        outcome.Reason.ShouldNotBeNull().ShouldContain("open-dispatch");
        h.Terminator.Killed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_Pinned_Quando_Hibernate_Entao_GuardaRecusa()
    {
        var h = new Harness();
        var state = h.State();
        h.States.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(state);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1"));
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", false, Now, Now, [], "native-1", "/w", 0, true);

        var outcome = await h.Service.HibernateAsync("local", Key);

        outcome.Hibernated.ShouldBeFalse();
        outcome.Reason.ShouldNotBeNull().ShouldContain("pinned");
        h.Terminator.Killed.ShouldBeEmpty();
    }

    // ---------- threshold / sweep ----------

    [Fact]
    public async Task Dado_IdleAbaixoDoThreshold_Quando_Scan_Entao_NaoHiberna()
    {
        var h = new Harness();
        // 29min idle < 30min threshold
        h.States.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([h.State(idleMinutes: 29)]);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1"));

        var n = await h.Service.ScanOnceAsync("local", TimeSpan.FromMinutes(30));

        n.ShouldBe(0);
        h.Terminator.Killed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_IdleNoThreshold_Quando_Scan_Entao_Hiberna()
    {
        var h = new Harness();
        // exactly 30min idle ≥ threshold (boundary is inclusive)
        h.States.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([h.State(idleMinutes: 30)]);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1", cwd: "/repo/w"));

        var n = await h.Service.ScanOnceAsync("local", TimeSpan.FromMinutes(30));

        n.ShouldBe(1);
        h.Terminator.Killed.ShouldBe([Key]);
        var record = h.Records.Rows[Key];
        record.IsHibernated.ShouldBeTrue();
        record.NativeSessionId.ShouldBe("native-1");
        record.Cwd.ShouldBe("/repo/w");
        record.ResumeArgv.ShouldBe(["claude", "--resume", "native-1"]);
        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.SessionHibernated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SessaoJaHibernada_Quando_Scan_Entao_Skip()
    {
        var h = new Harness();
        h.States.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([h.State(idleMinutes: 90)]);
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", true, Now, Now, [], "n1", "/w", 0, false);

        var n = await h.Service.ScanOnceAsync("local", TimeSpan.FromMinutes(30));

        n.ShouldBe(0);
        h.Terminator.Killed.ShouldBeEmpty();
    }

    // ---------- resume ----------

    [Fact]
    public async Task Dado_Hibernada_Quando_Resume_Entao_RespawnEnvEMarcaResumed()
    {
        var h = new Harness();
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", true, Now, Now,
            ["claude", "--resume", "native-1"], "native-1", "/repo/w", 0, false);

        var outcome = await h.Service.ResumeAsync("local", Key);

        outcome.Resumed.ShouldBeTrue();
        var call = h.Respawner.Calls.ShouldHaveSingleItem();
        call.Key.ShouldBe(Key);
        call.Cwd.ShouldBe("/repo/w");
        call.Argv.ShouldBe(["claude", "--resume", "native-1"]);
        h.Records.Rows[Key].IsHibernated.ShouldBeFalse();
        await h.Attention.Received(1).PublishAsync(
            Arg.Is<AttentionSignal>(s =>
                s.SuggestedState == AgentAttentionState.Working
                && s.Reason == "lifecycle:resumed"),
            Arg.Any<CancellationToken>());
        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.SessionResumed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Viva_Quando_Resume_Entao_IdempotenteSemRespawn()
    {
        var h = new Harness();
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", true, Now, Now,
            ["claude", "--resume", "n1"], "n1", "/w", 0, false);
        h.Terminator.Alive.Add(Key); // already alive — no dup spawn

        var outcome = await h.Service.ResumeAsync("local", Key);

        outcome.Resumed.ShouldBeTrue();
        h.Respawner.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RespawnFalha_Quando_Resume_Entao_BlockedEFeed()
    {
        var h = new Harness();
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", true, Now, Now,
            ["claude", "--resume", "n1"], "n1", "/w", 0, false);
        h.Respawner.NextResult = false;

        var outcome = await h.Service.ResumeAsync("local", Key);

        outcome.Resumed.ShouldBeFalse();
        await h.Attention.Received(1).PublishAsync(
            Arg.Is<AttentionSignal>(s =>
                s.SuggestedState == AgentAttentionState.Blocked
                && s.Reason == "resume_failed"),
            Arg.Any<CancellationToken>());
        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.Failure
                && r.NeedsAttention),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NaoHibernada_Quando_Resume_Entao_Recusa()
    {
        var h = new Harness();

        var outcome = await h.Service.ResumeAsync("local", Key);

        outcome.Resumed.ShouldBeFalse();
        outcome.Reason.ShouldNotBeNull().ShouldContain("not hibernated");
        h.Respawner.Calls.ShouldBeEmpty();
    }

    // ---------- pin ----------

    [Fact]
    public async Task Dado_VivaSemRegistro_Quando_Pin_Entao_CriaPinOnly()
    {
        var h = new Harness();
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("n1", cwd: "/repo/w"));

        var ok = await h.Service.SetPinnedAsync("local", Key, pinned: true);

        ok.ShouldBeTrue();
        var row = h.Records.Rows[Key];
        row.Pinned.ShouldBeTrue();
        row.IsHibernated.ShouldBeFalse();
        row.Cwd.ShouldBe("/repo/w");
    }

    [Fact]
    public async Task Dado_Pinned_Quando_Unpin_Entao_LimpaFlag()
    {
        var h = new Harness();
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", false, Now, Now, [], null, "/w", 0, true);

        await h.Service.SetPinnedAsync("local", Key, pinned: false);

        h.Records.Rows[Key].Pinned.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Pinned_Quando_Scan_Entao_NuncaHiberna()
    {
        var h = new Harness();
        h.States.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([h.State(idleMinutes: 120)]);
        h.Terminator.Alive.Add(Key);
        h.Index.ResolveByLiveKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(h.IndexRow("native-1"));
        h.Records.Rows[Key] = new AgentHibernationDto(
            Key, "local", "claude", false, Now, Now, [], "native-1", "/w", 0, true);

        var n = await h.Service.ScanOnceAsync("local", TimeSpan.FromMinutes(30));

        n.ShouldBe(0);
        h.Terminator.Killed.ShouldBeEmpty();
    }
}
