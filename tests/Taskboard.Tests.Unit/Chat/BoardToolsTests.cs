using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Integrations.Chat.Tools.Board;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261004-chat-board-tools: família board_* — issue no board (== GitHub),
/// mover card, labels, prioridade, close, comment e delegate encadeado.
/// </summary>
public class BoardToolsTests
{
    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static ChatToolContext Ctx(string? defaultAgentCli = null) =>
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
            DelegationDepth: 0,
            Activity: null,
            ToolSet: null,
            DefaultAgentCli: defaultAgentCli,
            DefaultAgentModel: null);

    private static IssueDto Issue(
        int number = 42,
        GitHubBoardColumn column = GitHubBoardColumn.Todo,
        string state = "open",
        string title = "Fix the thing") =>
        new(
            Id: number,
            Number: number,
            Title: title,
            Body: "body " + number,
            State: state,
            Url: $"https://api.github.com/repos/acme/app/issues/{number}",
            HtmlUrl: $"https://github.com/acme/app/issues/{number}",
            Labels: column.HasLabel() ? [column.ToLabel()] : ["bug"],
            Column: column,
            AssigneeLogin: null,
            Priority: "None",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow,
            ClosedAt: null);

    private static IServiceScopeFactory Scope(Action<IServiceCollection> register)
    {
        var sc = new ServiceCollection();
        register(sc);
        return sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // ---- board_list_issues (RF-001) ----

    [Fact]
    public async Task Dado_ListIssues_Quando_FiltroColuna_Entao_SoCardsDaColuna()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssuesAsync("acme/app", Arg.Any<CancellationToken>())
            .Returns([Issue(1, GitHubBoardColumn.Todo), Issue(2, GitHubBoardColumn.InReview)]);
        var tool = new BoardListIssuesTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","column":"in-review"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"number\":2");
        result.Json.ShouldNotContain("\"number\":1");
    }

    // ---- board_get_issue (RF-002) ----

    [Fact]
    public async Task Dado_GetIssue_Quando_Existe_Entao_CorpoEUltimosComentarios()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 7, Arg.Any<CancellationToken>()).Returns(Issue(7));
        github.GetIssueCommentsAsync("acme/app", 7, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new IssueCommentDto(1, "afonso", "first", DateTimeOffset.UtcNow, null, "https://x/c1")]);
        var tool = new BoardGetIssueTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":7}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("Fix the thing");
        result.Json.ShouldContain("first");
    }

    // ---- board_create_issue (RF-003) ----

    [Fact]
    public async Task Dado_CreateIssue_Quando_SemColuna_Entao_CriaEmTodo()
    {
        var github = Substitute.For<IGitHubService>();
        github.CreateIssueAsync("acme/app", "T", null, GitHubBoardColumn.Todo, Arg.Any<CancellationToken>())
            .Returns(Issue(9, GitHubBoardColumn.Todo));
        var tool = new BoardCreateIssueTool(github);
        tool.RequiresConfirmation.ShouldBeTrue();

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","title":"T"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"number\":9");
        await github.Received(1).CreateIssueAsync(
            "acme/app", "T", null, GitHubBoardColumn.Todo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CreateIssue_Quando_ColunaDerivada_Entao_Recusa()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardCreateIssueTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","title":"T","column":"archived"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await github.DidNotReceiveWithAnyArgs().CreateIssueAsync(default!, default!, default, default, default);
    }

    // ---- board_move_card (RF-004) ----

    [Fact]
    public async Task Dado_CardEmTodo_Quando_MoverParaReview_Entao_TrocaLabel()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 42, Arg.Any<CancellationToken>())
            .Returns(Issue(42, GitHubBoardColumn.Todo));
        github.UpdateIssueColumnAsync("acme/app", 42, GitHubBoardColumn.Todo, GitHubBoardColumn.InReview, Arg.Any<CancellationToken>())
            .Returns(Issue(42, GitHubBoardColumn.InReview));
        var tool = new BoardMoveCardTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":42,"column":"in-review"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await github.Received(1).UpdateIssueColumnAsync(
            "acme/app", 42, GitHubBoardColumn.Todo, GitHubBoardColumn.InReview, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RepositorioInvalido_Quando_MoverCard_Entao_RecusaSemChamarGithub()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardMoveCardTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"sem-dono","number":1,"column":"done"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("owner/name");
        await github.DidNotReceiveWithAnyArgs().GetIssueAsync(default!, default, default);
    }

    [Fact]
    public async Task Dado_IssueInexistente_Quando_MoverCard_Entao_Recusa()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 99, Arg.Any<CancellationToken>()).Returns((IssueDto?)null);
        var tool = new BoardMoveCardTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":99,"column":"done"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await github.DidNotReceiveWithAnyArgs().UpdateIssueColumnAsync(default!, default, default, default, default);
    }

    // ---- board_update_issue (RF-005) ----

    [Fact]
    public async Task Dado_UpdateIssue_Quando_SemCampos_Entao_Recusa()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardUpdateIssueTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":1}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await github.DidNotReceiveWithAnyArgs().UpdateIssueAsync(default!, default, default, default, default);
    }

    // ---- board_set_labels (RF-006) ----

    [Fact]
    public async Task Dado_SetLabels_Quando_AddTemLabelDeColuna_Entao_Recusa()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardSetLabelsTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":1,"add":["in-review","bug"]}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("board_move_card");
        await github.DidNotReceiveWithAnyArgs().AddLabelsToIssueAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Dado_SetLabels_Quando_AddERemove_Entao_ChamadasCertas()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardSetLabelsTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":1,"add":["backend"],"remove":["wontfix"]}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await github.Received(1).AddLabelsToIssueAsync(
            "acme/app", 1, Arg.Is<IReadOnlyCollection<string>>(l => l.Contains("backend")), Arg.Any<CancellationToken>());
        await github.Received(1).RemoveLabelsFromIssueAsync(
            "acme/app", 1, Arg.Is<IReadOnlyCollection<string>>(l => l.Contains("wontfix")), Arg.Any<CancellationToken>());
    }

    // ---- board_close_issue / board_comment (RF-007) ----

    [Fact]
    public async Task Dado_CloseIssue_Quando_Done_Entao_MoveParaDoneEFecha()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 5, Arg.Any<CancellationToken>())
            .Returns(Issue(5, GitHubBoardColumn.InProgress));
        github.CloseIssueAsync("acme/app", 5, "archived", Arg.Any<CancellationToken>())
            .Returns(Issue(5, GitHubBoardColumn.Done, state: "closed"));
        var tool = new BoardCloseIssueTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":5,"resolution":"done","reason":"shipped"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await github.Received(1).AddIssueCommentAsync("acme/app", 5, "shipped", Arg.Any<CancellationToken>());
        await github.Received(1).UpdateIssueColumnAsync(
            "acme/app", 5, GitHubBoardColumn.InProgress, GitHubBoardColumn.Done, Arg.Any<CancellationToken>());
        await github.Received(1).CloseIssueAsync("acme/app", 5, "archived", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CloseIssue_Quando_Canceled_Entao_FechaComLabelCanceled()
    {
        var github = Substitute.For<IGitHubService>();
        github.CloseIssueAsync("acme/app", 5, "canceled", Arg.Any<CancellationToken>())
            .Returns(Issue(5, GitHubBoardColumn.Canceled, state: "closed"));
        var tool = new BoardCloseIssueTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":5,"resolution":"canceled"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await github.Received(1).CloseIssueAsync("acme/app", 5, "canceled", Arg.Any<CancellationToken>());
        await github.DidNotReceiveWithAnyArgs().UpdateIssueColumnAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task Dado_Comment_Quando_Body_Entao_PostaComentario()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardCommentTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":3,"body":"on it"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await github.Received(1).AddIssueCommentAsync("acme/app", 3, "on it", Arg.Any<CancellationToken>());
    }

    // ---- board_set_priority (RF-008) ----

    [Fact]
    public async Task Dado_SetPriority_Quando_ValorInvalido_Entao_Recusa()
    {
        var github = Substitute.For<IGitHubService>();
        var tool = new BoardSetPriorityTool(github);

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":1,"priority":"super-urgent"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await github.DidNotReceiveWithAnyArgs().SetIssuePriorityAsync(default!, default, default!, default);
    }

    // ---- board_delegate_issue (RF-009) ----

    [Fact]
    public async Task Dado_DelegateIssue_Quando_Ok_Entao_CriaTaskWorktreeEMoveCard()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 42, Arg.Any<CancellationToken>())
            .Returns(Issue(42, GitHubBoardColumn.Todo));

        var provisioning = Substitute.For<IRepositoryProvisioningService>();
        provisioning.EnsureCloneAsync("acme/app", Arg.Any<CancellationToken>())
            .Returns(new RepositoryCloneResult("/repos/app", Cloned: true, 12));

        var delegation = Substitute.For<IDelegationService>();
        var task = new DelegationTaskDto(
            Id: "task-42x", Scope: "s", Prompt: "p", CliName: "opencode",
            DependsOn: [], RetryOf: null, FanoutGroupId: null, UseWorktree: true,
            WorktreeRunId: null, WorkspacePath: "/repos/app", RepositoryPath: "/repos/app",
            BaseCommitSha: null, Status: DelegationTaskStatus.Pending, ResultSummary: null,
            Error: null, CreatedAt: DateTime.UtcNow, StartedAt: null, FinishedAt: null,
            LastHeartbeatAt: null);
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(task);
        delegation.AttachWorktreeAsync("task-42x", "run-1", Arg.Any<CancellationToken>())
            .Returns(task);

        var isolation = Substitute.For<IWorkspaceIsolationService>();
        isolation.CreateWorktreeAsync("task-42x", "/repos/app", "main", Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                WorktreeId: "wt-1", RunId: "run-1", Path: "/wt", Branch: "b",
                Status: "active", RepositoryPath: "/repos/app", BaseBranch: "main",
                CommitSha: null, RetainOnFailure: false,
                CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, Version: 1));

        var tool = new BoardDelegateIssueTool(github, Scope(sc => sc
            .AddSingleton(provisioning)
            .AddSingleton(delegation)
            .AddSingleton(isolation)));

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":42,"cli":"opencode"}"""),
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("task-42x");

        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.Prompt.Contains("acme/app#42") && r.CliName == "opencode" && r.UseWorktree),
            Arg.Any<CancellationToken>());
        await isolation.Received(1).CreateWorktreeAsync(
            "task-42x", "/repos/app", "main", Arg.Is<string>(s => s.Contains("issue-42")), false, Arg.Any<CancellationToken>());
        await delegation.Received(1).AttachWorktreeAsync("task-42x", "run-1", Arg.Any<CancellationToken>());
        await github.Received(1).UpdateIssueColumnAsync(
            "acme/app", 42, GitHubBoardColumn.Todo, GitHubBoardColumn.InProgress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DelegateIssue_Quando_SemIssue_Entao_RecusaSemProvisionar()
    {
        var github = Substitute.For<IGitHubService>();
        github.GetIssueAsync("acme/app", 99, Arg.Any<CancellationToken>()).Returns((IssueDto?)null);
        var provisioning = Substitute.For<IRepositoryProvisioningService>();
        var tool = new BoardDelegateIssueTool(
            github,
            Scope(sc => sc
                .AddSingleton(provisioning)
                .AddSingleton(Substitute.For<IDelegationService>())
                .AddSingleton(Substitute.For<IWorkspaceIsolationService>())));

        var result = await tool.ExecuteAsync(
            Args("""{"repository":"acme/app","number":99}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        await provisioning.DidNotReceiveWithAnyArgs().EnsureCloneAsync(default!, default);
    }

    // ---- wiring (RF-010) ----

    [Fact]
    public void Dado_ProgramCs_Quando_Lido_Entao_BoardToolsRegistradas()
    {
        var program = RepoFile("src/Taskboard.Server/Program.cs");

        foreach (var tool in new[]
        {
            "BoardListIssuesTool", "BoardGetIssueTool", "BoardCreateIssueTool",
            "BoardMoveCardTool", "BoardUpdateIssueTool", "BoardSetLabelsTool",
            "BoardSetPriorityTool", "BoardCloseIssueTool", "BoardCommentTool",
            "BoardDelegateIssueTool"
        })
        {
            program.ShouldContain($"new {tool}(");
        }
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull();
        return File.ReadAllText(Path.Combine(dir.FullName, relative));
    }
}
