using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.GitHub;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261014-chat-git-bar-overview: gating de <see cref="ConversationGitService"/>
/// — chips de status, bloqueio por run ativa, create-PR idempotente e regra de
/// uncommitted-changes fora de worktree.
/// </summary>
public sealed class ConversationGitServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Join(Path.GetTempPath(), $"tb-git-{Guid.NewGuid()}.sqlite");
    private readonly string _workdir = Path.Combine(Path.GetTempPath(), $"gitbar-{Guid.NewGuid():N}");
    private readonly TaskboardDbContext _context;
    private readonly ChatConversation _conversation;
    private readonly FakeGitOps _git = new();
    private readonly IGitHubService _github = Substitute.For<IGitHubService>();
    private readonly IChatWorkspaceDiffService _diff = Substitute.For<IChatWorkspaceDiffService>();

    public ConversationGitServiceTests()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        Directory.CreateDirectory(_workdir);

        var provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(provider);
        _conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), provider.Id, "fake", "m1", "minha feature");
        _conversation.SetAgentContext(null, "afonsoft/agent-harness", _workdir, null);
        _context.ChatConversations.Add(_conversation);
        _context.SaveChanges();
    }

    private ConversationGitService NewService()
    {
        var ws = new ConversationWorkspaceService(
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatRun>(_context),
            new EfCoreRepository<ChatApproval>(_context),
            Substitute.For<IWorktreeSessionRepository>(),
            Substitute.For<IWorkspaceIsolationService>(),
            _diff,
            Substitute.For<IWorkspacePathResolver>(),
            new ChatTodoStore(_workdir));
        return new ConversationGitService(
            new EfCoreRepository<ChatConversation>(_context),
            new EfCoreRepository<ChatRun>(_context),
            ws,
            _git,
            _github,
            new ServiceCollection().AddHybridCache().Services.BuildServiceProvider()
                .GetRequiredService<HybridCache>());
    }

    [Fact]
    public async Task Dado_RepoComUpstream_Quando_Status_Entao_BranchAheadBehindSlug()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(IsGit: true, Branch: "main", Dirty: true));
        _git.Set("status -sb", new ChatGitCommandResult(0, "## main...origin/main [ahead 2, behind 1]\n M a.txt\n", "", false));
        _git.Slug = "afonsoft/agent-harness";

        var status = await NewService().GetStatusAsync(_conversation.Id.Value);

        status.ShouldNotBeNull();
        status.IsRepo.ShouldBeTrue();
        status.Branch.ShouldBe("main");
        status.Ahead.ShouldBe(2);
        status.Behind.ShouldBe(1);
        status.Dirty.ShouldBeTrue();
        status.RepoSlug.ShouldBe("afonsoft/agent-harness");
        status.IsWorktree.ShouldBeFalse();
        status.RunActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_DirForaDeGit_Quando_Status_Entao_IsRepoFalse()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>()).Returns((ChatWorkspaceStatusDto?)null);

        var status = await NewService().GetStatusAsync(_conversation.Id.Value);

        status.ShouldNotBeNull();
        status.IsRepo.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_RunAtiva_Quando_Pull_Entao_RecusaRunActiveSemGit()
    {
        var run = ChatRun.Create(ChatRunId.NewGuid(), _conversation.Id, ChatMessageId.NewGuid());
        run.Start();
        _context.ChatRuns.Add(run);
        await _context.SaveChangesAsync();
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));

        var result = await NewService().RunSyncOpAsync(_conversation.Id.Value, "pull");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("run-active");
        _git.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_WorkspaceLimpo_Quando_Pull_Entao_ExecutaGitPull()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));
        _git.Set("pull", new ChatGitCommandResult(0, "Already up to date.\n", "", false));

        var result = await NewService().RunSyncOpAsync(_conversation.Id.Value, "pull");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeTrue();
        _git.Calls.ShouldContain(c => c.Contains("pull"));
    }

    [Fact]
    public async Task Dado_MudancasSemCommit_Quando_CreatePr_Entao_UncommittedFiles()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", true));
        _git.Set("rev-parse --abbrev-ref HEAD", new ChatGitCommandResult(0, "feat/x\n", "", false));
        _git.Set("status --porcelain", new ChatGitCommandResult(0, " M a.txt\n?? b.txt\n", "", false));

        var result = await NewService().CreatePullRequestAsync(
            _conversation.Id.Value, new CreateChatPullRequestRequest());

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("uncommitted-changes");
        result.UncommittedFiles.ShouldNotBeNull();
        result.UncommittedFiles.Count.ShouldBe(2);
        await _github.DidNotReceive().CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_WorkspaceLimpo_Quando_CreatePr_Entao_CriaPr()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "feat/x", false));
        _git.Set("rev-parse --abbrev-ref HEAD", new ChatGitCommandResult(0, "feat/x\n", "", false));
        _git.Set("status --porcelain", new ChatGitCommandResult(0, "", "", false));
        _git.Set("symbolic-ref --short refs/remotes/origin/HEAD", new ChatGitCommandResult(0, "origin/main\n", "", false));
        _git.Slug = "afonsoft/agent-harness";
        _github.FindOpenPullRequestUrlAsync("afonsoft/agent-harness", "feat/x", "main", Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _github.CreatePullRequestAsync(
                "afonsoft/agent-harness", Arg.Any<string>(), "feat/x", "main",
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/afonsoft/agent-harness/pull/42");

        var result = await NewService().CreatePullRequestAsync(
            _conversation.Id.Value, new CreateChatPullRequestRequest());

        result.ShouldNotBeNull();
        result.Ok.ShouldBeTrue();
        result.Existing.ShouldBeFalse();
        result.Url.ShouldBe("https://github.com/afonsoft/agent-harness/pull/42");
        result.Number.ShouldBe(42);
    }

    [Fact]
    public async Task Dado_PrJaAberto_Quando_CreatePr_Entao_Idempotente()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "feat/x", false));
        _git.Set("rev-parse --abbrev-ref HEAD", new ChatGitCommandResult(0, "feat/x\n", "", false));
        _git.Set("status --porcelain", new ChatGitCommandResult(0, "", "", false));
        _git.Set("symbolic-ref --short refs/remotes/origin/HEAD", new ChatGitCommandResult(0, "origin/main\n", "", false));
        _git.Slug = "afonsoft/agent-harness";
        _github.FindOpenPullRequestUrlAsync("afonsoft/agent-harness", "feat/x", "main", Arg.Any<CancellationToken>())
            .Returns("https://github.com/afonsoft/agent-harness/pull/7");

        var result = await NewService().CreatePullRequestAsync(
            _conversation.Id.Value, new CreateChatPullRequestRequest());

        result.ShouldNotBeNull();
        result.Ok.ShouldBeTrue();
        result.Existing.ShouldBeTrue();
        result.Number.ShouldBe(7);
        await _github.DidNotReceive().CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_UrlInvalida_Quando_PrCard_Entao_Null()
    {
        var card = await NewService().GetPrCardAsync("https://example.com/not-a-pr");

        card.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RefsLocaisERemotas_Quando_Branches_Entao_ListaDedupSemHead()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(IsGit: true, Branch: "main", Dirty: false));
        _git.Set("for-each-ref --format=%(refname) refs/heads/ refs/remotes/",
            new ChatGitCommandResult(0, "refs/heads/feature-x\nrefs/heads/feature/slash\nrefs/heads/main\nrefs/remotes/origin/HEAD\nrefs/remotes/origin/main\nrefs/remotes/origin/release\n", "", false));
        _git.Set("branch --show-current", new ChatGitCommandResult(0, "main\n", "", false));

        var branches = await NewService().GetBranchesAsync(_conversation.Id.Value);

        branches.ShouldNotBeNull();
        branches.Current.ShouldBe("main");
        branches.Branches.ShouldBe(["feature-x", "feature/slash", "main", "release"]);
    }

    [Fact]
    public async Task Dado_DirForaDeGit_Quando_Branches_Entao_ListaVazia()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>()).Returns((ChatWorkspaceStatusDto?)null);

        var branches = await NewService().GetBranchesAsync(_conversation.Id.Value);

        branches.ShouldNotBeNull();
        branches.Branches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RunAtiva_Quando_Checkout_Entao_RecusaRunActive()
    {
        var run = ChatRun.Create(ChatRunId.NewGuid(), _conversation.Id, ChatMessageId.NewGuid());
        run.Start();
        _context.ChatRuns.Add(run);
        await _context.SaveChangesAsync();
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));

        var result = await NewService().CheckoutAsync(_conversation.Id.Value, "feature-x");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("run-active");
        _git.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_BranchComFlag_Quando_Checkout_Entao_InvalidBranchSemGit()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));

        var result = await NewService().CheckoutAsync(_conversation.Id.Value, "--detach");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("invalid-branch");
        _git.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_WorkspaceLimpo_Quando_Checkout_Entao_SwitchEPull()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));
        _git.Set("switch feature-x", new ChatGitCommandResult(0, "Switched to branch 'feature-x'\n", "", false));
        _git.Set("pull", new ChatGitCommandResult(0, "Already up to date.\n", "", false));

        var result = await NewService().CheckoutAsync(_conversation.Id.Value, "feature-x");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeTrue();
        result.Error.ShouldBeNull();
        _git.Calls.ShouldBe(["switch feature-x", "pull"]);
    }

    [Fact]
    public async Task Dado_SwitchFalha_Quando_Checkout_Entao_GitFailedSemPull()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));
        _git.Set("switch ghost", new ChatGitCommandResult(128, "", "fatal: invalid reference\n", false));

        var result = await NewService().CheckoutAsync(_conversation.Id.Value, "ghost");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("git-failed");
        _git.Calls.ShouldNotContain("pull");
    }

    [Fact]
    public async Task Dado_PullFalhaAposSwitch_Quando_Checkout_Entao_OkComPullFailed()
    {
        _diff.StatusAsync(_workdir, Arg.Any<CancellationToken>())
            .Returns(new ChatWorkspaceStatusDto(true, "main", false));
        _git.Set("switch feature-x", new ChatGitCommandResult(0, "Switched to branch 'feature-x'\n", "", false));
        _git.Set("pull", new ChatGitCommandResult(1, "", "no tracking information\n", false));

        var result = await NewService().CheckoutAsync(_conversation.Id.Value, "feature-x");

        result.ShouldNotBeNull();
        result.Ok.ShouldBeTrue();
        result.Error.ShouldBe("pull-failed");
    }

    public void Dispose()
    {
        _context.Dispose();
        TryDelete(_dbPath);
        try { Directory.Delete(_workdir, recursive: true); } catch { /* best-effort */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* locked/missing — temp */ }
    }

    /// <summary>Scripted <see cref="IChatGitOps"/> — answers by joined args.</summary>
    private sealed class FakeGitOps : IChatGitOps
    {
        private readonly Dictionary<string, ChatGitCommandResult> _scripts = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];
        public string? Slug { get; set; }

        public void Set(string args, ChatGitCommandResult result) => _scripts[args] = result;

        public Task<ChatGitCommandResult> RunAsync(
            string workdir, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            var joined = string.Join(' ', args);
            Calls.Add(joined);
            return Task.FromResult(_scripts.TryGetValue(joined, out var result)
                ? result
                : new ChatGitCommandResult(1, "", $"no fake for '{joined}'", false));
        }

        public Task<string?> ResolveRemoteSlugAsync(string workdir, CancellationToken cancellationToken = default)
            => Task.FromResult(Slug);
    }
}
