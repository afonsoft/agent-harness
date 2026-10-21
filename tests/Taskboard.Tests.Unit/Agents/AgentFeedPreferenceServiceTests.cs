using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-006: merged per-user category
/// preferences — defaults, overrides, cache invalidation on write.
/// </summary>
public class AgentFeedPreferenceServiceTests
{
    private static (AgentFeedPreferenceService Svc, IUserFeedPreferenceRepository Repo) NewSut(
        IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference>? stored = null)
    {
        var rows = new Dictionary<AgentFeedKind, FeedCategoryPreference>(
            stored ?? new Dictionary<AgentFeedKind, FeedCategoryPreference>());
        var repo = Substitute.For<IUserFeedPreferenceRepository>();
        repo.GetForUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference>>(rows));
        repo.UpsertAsync(
                Arg.Any<string>(), Arg.Any<AgentFeedKind>(),
                Arg.Any<FeedCategoryPreference>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                rows[ci.Arg<AgentFeedKind>()] = ci.Arg<FeedCategoryPreference>();
                return Task.CompletedTask;
            });

        var services = new ServiceCollection()
            .AddSingleton(repo)
            .BuildServiceProvider();
        return (new AgentFeedPreferenceService(services), repo);
    }

    [Fact]
    public async Task Dado_SemOverrides_Quando_GetForUser_Entao_DefaultsCompletos()
    {
        var (svc, _) = NewSut();

        var prefs = await svc.GetForUserAsync("local");

        prefs.Categories.Count.ShouldBe(Enum.GetValues<AgentFeedKind>().Length);
        prefs.Categories[AgentFeedKind.Attention]
            .ShouldBe(AgentFeedDefaults.For(AgentFeedKind.Attention));
        prefs.Categories[AgentFeedKind.Pr]
            .ShouldBe(AgentFeedDefaults.For(AgentFeedKind.Pr));
    }

    [Fact]
    public async Task Dado_OverrideArmazenado_Quando_Resolve_Entao_OverrideVence()
    {
        var stored = new Dictionary<AgentFeedKind, FeedCategoryPreference>
        {
            [AgentFeedKind.Attention] = new(Badge: false, Sound: false, Persist: false),
        };
        var (svc, _) = NewSut(stored);

        var pref = await svc.ResolveAsync("local", AgentFeedKind.Attention);

        pref.Persist.ShouldBeFalse();
        pref.Badge.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_CachePopulado_Quando_Save_Entao_InvalidaEUpsert()
    {
        var (svc, repo) = NewSut();
        await svc.GetForUserAsync("local"); // populates the cache

        var updated = await svc.SaveForUserAsync(
            "local",
            new Dictionary<AgentFeedKind, FeedCategoryPreference>
            {
                [AgentFeedKind.Completion] = new(Badge: false, Sound: true, Persist: true),
            });

        await repo.Received(1).UpsertAsync(
            "local", AgentFeedKind.Completion,
            Arg.Is<FeedCategoryPreference>(p => p.Sound && !p.Badge && p.Persist),
            Arg.Any<CancellationToken>());
        updated.Categories[AgentFeedKind.Completion].Sound.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DuasLeituras_Quando_GetForUser_Entao_UmaConsultaAoRepo()
    {
        var (svc, repo) = NewSut();

        await svc.GetForUserAsync("local");
        await svc.GetForUserAsync("local");

        await repo.Received(1).GetForUserAsync("local", Arg.Any<CancellationToken>());
    }
}
