using Shouldly;
using Taskboard;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class ProjectMemoryItemTests
{
    [Fact]
    public void Dado_DadosValidos_Quando_Create_Entao_ItemAtivoComTags()
    {
        var item = ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            "afonsoft/taskboard-ai",
            "build-system",
            "Limpar _framework antes de dotnet publish",
            MemoryType.LessonLearned,
            ["build", "blazor"]);

        item.Id.Value.ShouldNotBeNullOrWhiteSpace();
        item.RepositoryFullName.ShouldBe("afonsoft/taskboard-ai");
        item.Topic.ShouldBe("build-system");
        item.Type.ShouldBe(MemoryType.LessonLearned);
        item.Tags.ShouldBe(["build", "blazor"]);
        item.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Dado_ContentVazio_Quando_Create_Entao_DomainException(string? content)
    {
        Should.Throw<DomainException>(() => ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            "afonsoft/taskboard-ai",
            "topic",
            content!,
            MemoryType.Fact,
            []));
    }

    [Fact]
    public void Dado_RepoInvalido_Quando_Create_Entao_DomainException()
    {
        Should.Throw<DomainException>(() => ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            "sem-dono",
            "topic",
            "fact",
            MemoryType.Fact,
            []));
    }

    [Fact]
    public void Dado_TagsComDuplicatas_Quando_Create_Entao_Normaliza()
    {
        var item = ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            "owner/repo",
            "t",
            "c",
            MemoryType.Fact,
            ["Build", " build ", "TESTS"]);

        item.Tags.ShouldBe(["build", "tests"]);
    }

    [Fact]
    public void Dado_Item_Quando_UpdateContent_Entao_AtualizaEIncrementaVersao()
    {
        var item = ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(), "o/r", "t", "old", MemoryType.Fact, []);

        item.UpdateContent("new", ["x"]);

        item.Content.ShouldBe("new");
        item.Tags.ShouldBe(["x"]);
        item.Version.ShouldBe(2);
    }
}
