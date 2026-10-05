using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261004-promote-leg-pr: remote → slug parsing, base normalization and
/// the promoted-leg → GitHub PR step. IGitHubService is mocked — no real
/// GitHub calls in tests.
/// </summary>
public class PromotedLegPrServiceTests
{
    private static DelegationTaskDto Task(string? repoPath = "/repos/owner-repo") =>
        new("task-1", "scope", "fix the flaky login test", "opencode", [],
            null, null, true, "run-1", "/ws", repoPath, "abc123",
            Taskboard.Delegation.DelegationTaskStatus.Done, null, null,
            DateTime.UtcNow, null, null, null);

    private static WorktreeSessionDto Session(string? baseBranch = "origin/main") =>
        new("wt-1", "run-1", "/repos/wt", "harness/task-1", "active",
            "/repos/owner-repo", baseBranch!, "deadbeef", false,
            DateTime.UtcNow, DateTime.UtcNow, 1);

    private static IWorkspaceIsolationService Isolation(WorktreeSessionDto session)
    {
        var isolation = Substitute.For<IWorkspaceIsolationService>();
        isolation.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(session);
        return isolation;
    }

    private static IGitCommandRunner Git(string remoteUrl = "https://github.com/af/repo.git", string? originHead = "origin/main")
    {
        var git = Substitute.For<IGitCommandRunner>();
        git.RunAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "config", "remote.origin.url" })),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, remoteUrl, string.Empty, false));
        git.RunAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(a => a.Count > 0 && a[0] == "symbolic-ref"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(originHead is null
                ? new GitCommandResult(1, string.Empty, "err", false)
                : new GitCommandResult(0, originHead, string.Empty, false));
        return git;
    }

    private static IPromotedLegPrService Sut(
        IWorkspaceIsolationService isolation, IGitCommandRunner git, IGitHubService gh) =>
        new PromotedLegPrService(isolation, git, gh);

    [Theory]
    [InlineData("https://github.com/af/repo.git", "af/repo")]
    [InlineData("https://github.com/af/repo", "af/repo")]
    [InlineData("git@github.com:af/repo.git", "af/repo")]
    [InlineData("ssh://git@github.com/af/repo.git", "af/repo")]
    [InlineData("https://github.com/AF/Repo/", "AF/Repo")]
    public void Dado_RemoteGithub_Quando_TryParse_Entao_Slug(string url, string expected)
    {
        GitRemoteSlug.TryParse(url).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://gitlab.com/af/repo.git")]
    [InlineData("not-a-url")]
    [InlineData("https://github.com/repo")]
    public void Dado_RemoteInvalido_Quando_TryParse_Entao_Null(string? url)
    {
        GitRemoteSlug.TryParse(url!).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_LegComRepo_Quando_TryCreate_Entao_PrUrl()
    {
        var git = Git();
        var gh = Substitute.For<IGitHubService>();
        gh.CreatePullRequestAsync("af/repo", Arg.Any<string>(), "harness/task-1", "main",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/af/repo/pull/42");
        var sut = Sut(Isolation(Session()), git, gh);

        var result = await sut.TryCreateAsync(
            Task(), null, null, CancellationToken.None);

        result.PullRequestUrl.ShouldBe("https://github.com/af/repo/pull/42");
        result.Error.ShouldBeNull();
        await gh.Received(1).CreatePullRequestAsync(
            "af/repo", Arg.Is<string>(t => t.Contains("opencode")),
            "harness/task-1", "main", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_BaseExplicita_Quando_TryCreate_Entao_UsaBase()
    {
        var gh = Substitute.For<IGitHubService>();
        gh.CreatePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("url");
        var sut = Sut(Isolation(Session()), Git(), gh);

        await sut.TryCreateAsync(Task(), null, "develop", CancellationToken.None);

        await gh.Received(1).CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "develop", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskSemRepoPath_Quando_TryCreate_Entao_SlugUnresolved()
    {
        var sut = Sut(Isolation(Session()), Git(), Substitute.For<IGitHubService>());

        var result = await sut.TryCreateAsync(
            Task(repoPath: null), null, null, CancellationToken.None);

        result.Error.ShouldBe(PromotedLegPrError.SlugUnresolved);
        result.PullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RemoteNaoGithub_Quando_TryCreate_Entao_SlugUnresolved()
    {
        var sut = Sut(Isolation(Session()), Git(remoteUrl: "https://gitlab.com/af/repo.git"),
            Substitute.For<IGitHubService>());

        var result = await sut.TryCreateAsync(Task(), null, null, CancellationToken.None);

        result.Error.ShouldBe(PromotedLegPrError.SlugUnresolved);
    }

    [Fact]
    public async Task Dado_GitHubFalha_Quando_TryCreate_Entao_GithubFailed()
    {
        var gh = Substitute.For<IGitHubService>();
        gh.CreatePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("401"));
        var sut = Sut(Isolation(Session()), Git(), gh);

        var result = await sut.TryCreateAsync(Task(), null, null, CancellationToken.None);

        result.Error.ShouldBe(PromotedLegPrError.GithubFailed);
        result.PullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_BaseBranchOrigin_Quando_TryCreate_Entao_Normaliza()
    {
        var gh = Substitute.For<IGitHubService>();
        gh.CreatePullRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("url");
        var sut = Sut(Isolation(Session(baseBranch: "origin/develop")), Git(), gh);

        await sut.TryCreateAsync(Task(), null, null, CancellationToken.None);

        await gh.Received(1).CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "develop", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
