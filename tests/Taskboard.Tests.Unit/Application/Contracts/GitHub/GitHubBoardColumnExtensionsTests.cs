using Shouldly;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Application.Contracts.GitHub;

public class GitHubBoardColumnExtensionsTests
{
    [Theory]
    [InlineData(GitHubBoardColumn.Backlog, "backlog")]
    [InlineData(GitHubBoardColumn.Todo, "todo")]
    [InlineData(GitHubBoardColumn.InProgress, "in-progress")]
    [InlineData(GitHubBoardColumn.InReview, "in-review")]
    [InlineData(GitHubBoardColumn.InPullRequest, "in-pullrequest")]
    [InlineData(GitHubBoardColumn.Blocked, "blocked")]
    [InlineData(GitHubBoardColumn.Done, "done")]
    [InlineData(GitHubBoardColumn.Canceled, "canceled")]
    public void Dado_ColunaComLabel_Quando_ConverterParaLabel_Entao_RetornaLabelCorrespondente(
        GitHubBoardColumn column,
        string expectedLabel)
    {
        column.ToLabel().ShouldBe(expectedLabel);
    }

    [Fact]
    public void Dado_ColunaArchived_Quando_ConverterParaLabel_Entao_LancaExcecao()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GitHubBoardColumn.Archived.ToLabel());
    }

    [Theory]
    [InlineData(GitHubBoardColumn.Backlog, "backlog")]
    [InlineData(GitHubBoardColumn.Todo, "todo")]
    [InlineData(GitHubBoardColumn.InProgress, "in_progress")]
    [InlineData(GitHubBoardColumn.InReview, "in_review")]
    [InlineData(GitHubBoardColumn.InPullRequest, "in_pullrequest")]
    [InlineData(GitHubBoardColumn.Blocked, "blocked")]
    [InlineData(GitHubBoardColumn.Done, "done")]
    [InlineData(GitHubBoardColumn.Canceled, "canceled")]
    [InlineData(GitHubBoardColumn.Archived, "archived")]
    public void Dado_Coluna_Quando_ObterNomeExibicao_Entao_RetornaNomeDeStatus(
        GitHubBoardColumn column,
        string expectedName)
    {
        column.ToDisplayName().ShouldBe(expectedName);
    }

    [Theory]
    [InlineData("backlog", GitHubBoardColumn.Backlog)]
    [InlineData("todo", GitHubBoardColumn.Todo)]
    [InlineData("in-progress", GitHubBoardColumn.InProgress)]
    [InlineData("in-review", GitHubBoardColumn.InReview)]
    [InlineData("in-pullrequest", GitHubBoardColumn.InPullRequest)]
    [InlineData("blocked", GitHubBoardColumn.Blocked)]
    [InlineData("done", GitHubBoardColumn.Done)]
    [InlineData("canceled", GitHubBoardColumn.Canceled)]
    [InlineData("review", GitHubBoardColumn.InReview)]
    [InlineData("BACKLOG", GitHubBoardColumn.Backlog)]
    [InlineData("In-Progress", GitHubBoardColumn.InProgress)]
    public void Dado_LabelValida_Quando_IdentificarColuna_Entao_RetornaColunaCorrespondente(
        string label,
        GitHubBoardColumn expectedColumn)
    {
        GitHubBoardColumnExtensions.FromLabel(label).ShouldBe(expectedColumn);
    }

    [Fact]
    public void Dado_LabelNaoMapeada_Quando_IdentificarColuna_Entao_RetornaNulo()
    {
        GitHubBoardColumnExtensions.FromLabel("invalid-label").ShouldBeNull();
    }

    [Fact]
    public void Dado_MapeamentoDeColunas_Quando_ObterTodasLabels_Entao_RetornaOitoLabelsEsperadas()
    {
        var labels = GitHubBoardColumnExtensions.GetAllLabels();

        labels.Count.ShouldBe(8);
        labels.ShouldContain("backlog");
        labels.ShouldContain("todo");
        labels.ShouldContain("in-progress");
        labels.ShouldContain("in-review");
        labels.ShouldContain("in-pullrequest");
        labels.ShouldContain("blocked");
        labels.ShouldContain("done");
        labels.ShouldContain("canceled");
    }

    [Fact]
    public void Dado_ColunasComLabel_Quando_Listar_Entao_ExcluiArchived()
    {
        GitHubBoardColumnExtensions.LabelBackedColumns.ShouldNotContain(GitHubBoardColumn.Archived);
        GitHubBoardColumnExtensions.LabelBackedColumns.Count.ShouldBe(8);
    }

    [Theory]
    [InlineData(new[] { "priority:urgent" }, "Urgent")]
    [InlineData(new[] { "priority:high" }, "High")]
    [InlineData(new[] { "priority:medium" }, "Medium")]
    [InlineData(new[] { "priority:low" }, "Low")]
    [InlineData(new[] { "bug", "PRIORITY:HIGH" }, "High")]
    [InlineData(new[] { "bug" }, "None")]
    [InlineData(new string[0], "None")]
    public void Dado_Labels_Quando_ResolverPrioridade_Entao_RetornaPrioridadeEsperada(
        string[] labels,
        string expected)
    {
        GitHubBoardColumnExtensions.ResolvePriority(labels).ShouldBe(expected);
    }
}
