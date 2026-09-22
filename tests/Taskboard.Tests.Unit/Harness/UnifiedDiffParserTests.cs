using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260921-cockpit-live-logs-explorer-diff RF-004: split do patch por arquivo.</summary>
public class UnifiedDiffParserTests
{
    [Fact]
    public void Dado_PatchComDoisArquivos_Quando_Split_Entao_DuasSecoesComPaths()
    {
        var patch = """
            diff --git a/README.md b/README.md
            index 111..222 100644
            --- a/README.md
            +++ b/README.md
            @@ -1 +1 @@
            -old
            +new
            diff --git a/src/App.cs b/src/App.cs
            new file mode 100644
            --- /dev/null
            +++ b/src/App.cs
            @@ -0,0 +1 @@
            +class App {}
            """;

        var sections = UnifiedDiffParser.SplitByFile(patch);

        sections.Count.ShouldBe(2);
        sections[0].Path.ShouldBe("README.md");
        sections[0].Body.ShouldContain("-old");
        sections[1].Path.ShouldBe("src/App.cs");
        sections[1].Body.ShouldContain("+class App {}");
    }

    [Fact]
    public void Dado_ArquivoDeletado_Quando_Split_Entao_PathDoLadoAntigo()
    {
        var patch = """
            diff --git a/velho.cs b/velho.cs
            deleted file mode 100644
            --- a/velho.cs
            +++ /dev/null
            @@ -1 +0,0 @@
            -class Velho {}
            """;

        var sections = UnifiedDiffParser.SplitByFile(patch);

        sections.Count.ShouldBe(1);
        sections[0].Path.ShouldBe("velho.cs");
    }

    [Fact]
    public void Dado_ConteudoAntesDoPrimeiroHeader_Quando_Split_Entao_BucketSemPath()
    {
        var patch = "warning: LF will be replaced\ndiff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n";

        var sections = UnifiedDiffParser.SplitByFile(patch);

        sections.Count.ShouldBe(2);
        sections[0].Path.ShouldBeNull();
        sections[0].Body.ShouldContain("warning");
        sections[1].Path.ShouldBe("f.txt");
    }

    [Fact]
    public void Dado_PatchVazio_Quando_Split_Entao_ListaVazia()
    {
        UnifiedDiffParser.SplitByFile(null).ShouldBeEmpty();
        UnifiedDiffParser.SplitByFile("").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_Rename_Quando_Split_Entao_PathNovo()
    {
        var patch = """
            diff --git a/old.cs b/new.cs
            similarity index 90%
            rename from old.cs
            rename to new.cs
            --- a/old.cs
            +++ b/new.cs
            @@ -1 +1 @@
            -a
            +b
            """;

        UnifiedDiffParser.SplitByFile(patch)[0].Path.ShouldBe("new.cs");
    }
}
