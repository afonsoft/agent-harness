using Shouldly;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Application.Contracts.GitHub;

public class GitHubBoardGrouperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static IssueDto CriarIssue(
        int number,
        string state = "open",
        IReadOnlyList<string>? labels = null,
        DateTimeOffset? closedAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var issue = new IssueDto(
            number,
            number,
            $"Issue {number}",
            null,
            state,
            $"url-{number}",
            $"html-{number}",
            labels ?? [],
            GitHubBoardColumn.Backlog,
            null,
            GitHubBoardColumnExtensions.ResolvePriority(labels ?? []),
            Now.AddDays(-10),
            updatedAt ?? Now.AddDays(-1),
            closedAt);

        return issue with { Column = GitHubBoardGrouper.ResolveColumn(issue) };
    }

    [Fact]
    public void Dado_IssueAbertaSemLabel_Quando_ResolverColuna_Entao_Backlog()
    {
        var issue = CriarIssue(1);

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.Backlog);
    }

    [Theory]
    [InlineData("todo", GitHubBoardColumn.Todo)]
    [InlineData("in-progress", GitHubBoardColumn.InProgress)]
    [InlineData("in-review", GitHubBoardColumn.InReview)]
    [InlineData("in-pullrequest", GitHubBoardColumn.InPullRequest)]
    [InlineData("blocked", GitHubBoardColumn.Blocked)]
    [InlineData("done", GitHubBoardColumn.Done)]
    [InlineData("canceled", GitHubBoardColumn.Canceled)]
    public void Dado_IssueAbertaComLabel_Quando_ResolverColuna_Entao_ColunaCorrespondente(
        string label,
        GitHubBoardColumn expected)
    {
        var issue = CriarIssue(1, labels: [label]);

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(expected);
    }

    [Fact]
    public void Dado_IssueAbertaComLabelLegadaReview_Quando_ResolverColuna_Entao_InReview()
    {
        var issue = CriarIssue(1, labels: ["review"]);

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.InReview);
    }

    [Fact]
    public void Dado_IssueAbertaComMultiplasLabels_Quando_ResolverColuna_Entao_MaiorPrecedencia()
    {
        var issue = CriarIssue(1, labels: ["backlog", "in-review", "todo"]);

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.InReview);
    }

    [Fact]
    public void Dado_IssueFechadaComLabelDone_Quando_ResolverColuna_Entao_Done()
    {
        var issue = CriarIssue(1, state: "closed", labels: ["done"], closedAt: Now.AddDays(-1));

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.Done);
    }

    [Fact]
    public void Dado_IssueFechadaComLabelCanceled_Quando_ResolverColuna_Entao_Canceled()
    {
        var issue = CriarIssue(1, state: "closed", labels: ["canceled"], closedAt: Now.AddDays(-1));

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.Canceled);
    }

    [Fact]
    public void Dado_IssueFechadaSemLabelDeConclusao_Quando_ResolverColuna_Entao_Archived()
    {
        var issue = CriarIssue(1, state: "closed", labels: ["in-progress"], closedAt: Now.AddDays(-1));

        GitHubBoardGrouper.ResolveColumn(issue).ShouldBe(GitHubBoardColumn.Archived);
    }

    [Fact]
    public void Dado_IssueAberta_Quando_VerificarVisibilidade_Entao_Visivel()
    {
        GitHubBoardGrouper.IsVisible(CriarIssue(1), Now).ShouldBeTrue();
    }

    [Fact]
    public void Dado_IssueFechadaHa30Dias_Quando_VerificarVisibilidade_Entao_Visivel()
    {
        var issue = CriarIssue(1, state: "closed", closedAt: Now.AddDays(-30));

        GitHubBoardGrouper.IsVisible(issue, Now).ShouldBeTrue();
    }

    [Fact]
    public void Dado_IssueFechadaHa91Dias_Quando_VerificarVisibilidade_Entao_Oculta()
    {
        var issue = CriarIssue(1, state: "closed", closedAt: Now.AddDays(-91));

        GitHubBoardGrouper.IsVisible(issue, Now).ShouldBeFalse();
    }

    [Fact]
    public void Dado_IssueFechadaNoLimite_Quando_VerificarVisibilidade_Entao_Visivel()
    {
        var issue = CriarIssue(1, state: "closed", closedAt: Now.AddDays(-90));

        GitHubBoardGrouper.IsVisible(issue, Now).ShouldBeTrue();
    }

    [Fact]
    public void Dado_IssuesMisturadas_Quando_Agrupar_Entao_NoveColunasNaOrdemDoEnum()
    {
        var issues = new[]
        {
            CriarIssue(1),
            CriarIssue(2, labels: ["todo"]),
            CriarIssue(3, labels: ["in-progress"]),
            CriarIssue(4, state: "closed", labels: ["done"], closedAt: Now.AddDays(-1)),
            CriarIssue(5, state: "closed", labels: [], closedAt: Now.AddDays(-1)),
            CriarIssue(6, state: "closed", labels: ["done"], closedAt: Now.AddDays(-120))
        };

        var columns = GitHubBoardGrouper.GroupByColumn(issues, Now);

        columns.Count.ShouldBe(9);
        columns.Select(c => c.Column).ShouldBe(Enum.GetValues<GitHubBoardColumn>());
        columns.Single(c => c.Column == GitHubBoardColumn.Backlog).Issues.Count.ShouldBe(1);
        columns.Single(c => c.Column == GitHubBoardColumn.Todo).Issues.Count.ShouldBe(1);
        columns.Single(c => c.Column == GitHubBoardColumn.Done).Issues.Count.ShouldBe(1);
        columns.Single(c => c.Column == GitHubBoardColumn.Archived).Issues.Count.ShouldBe(1);
        columns.SelectMany(c => c.Issues).Count().ShouldBe(5);
    }

    [Fact]
    public void Dado_FiltroDeTexto_Quando_Filtrar_Entao_BuscaTituloLabelENumero()
    {
        var porTitulo = CriarIssue(1);
        var porLabel = CriarIssue(2, labels: ["backend"]);
        var porNumero = CriarIssue(42);

        GitHubBoardGrouper.MatchesFilters(porTitulo, "issue 1", null).ShouldBeTrue();
        GitHubBoardGrouper.MatchesFilters(porLabel, "BACKEND", null).ShouldBeTrue();
        GitHubBoardGrouper.MatchesFilters(porNumero, "42", null).ShouldBeTrue();
        GitHubBoardGrouper.MatchesFilters(porTitulo, "inexistente", null).ShouldBeFalse();
    }

    [Fact]
    public void Dado_FiltroDePrioridade_Quando_Filtrar_Entao_SomentePrioridadeCorrespondente()
    {
        var alta = CriarIssue(1, labels: ["priority:high"]);
        var semPrioridade = CriarIssue(2);

        GitHubBoardGrouper.MatchesFilters(alta, null, "High").ShouldBeTrue();
        GitHubBoardGrouper.MatchesFilters(semPrioridade, null, "High").ShouldBeFalse();
        GitHubBoardGrouper.MatchesFilters(semPrioridade, null, "None").ShouldBeTrue();
        GitHubBoardGrouper.MatchesFilters(alta, null, null).ShouldBeTrue();
    }
}
