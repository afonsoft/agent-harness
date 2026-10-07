using Shouldly;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261011-chat-workspace-panel: parsing compartilhado dos produtores
/// de WorkspaceDiffDto — name-status, porcelain, numstat e contagens de
/// untracked/binário.
/// </summary>
public class GitDiffParserTests : IDisposable
{
    private readonly string _workdir = Path.Combine(Path.GetTempPath(), $"gitdiffparse-{Guid.NewGuid():N}");

    public GitDiffParserTests() => Directory.CreateDirectory(_workdir);

    [Fact]
    public void Dado_NameStatus_Quando_Parse_Entao_MapeiaStatusERenomeia()
    {
        var files = GitDiffParser.ParseNameStatus("M\tsrc/a.cs\nA\tnew.cs\nD\told.cs\nR100\tsrc/old.cs\tsrc/new.cs\n");

        files.Count.ShouldBe(4);
        files[0].Path.ShouldBe("src/a.cs");
        files[0].Status.ShouldBe("Modified");
        files[1].Status.ShouldBe("Added");
        files[2].Status.ShouldBe("Deleted");
        files[3].Status.ShouldBe("Renamed");
        files[3].Path.ShouldBe("src/new.cs", "rename resolve para o path novo");
    }

    [Fact]
    public void Dado_Porcelain_Quando_ParseStatus_Entao_UntrackedEStatus()
    {
        var files = GitDiffParser.ParseStatus(" M src/a.cs\n?? b.txt\nAM c.cs\nR  o.cs -> n.cs\n");

        files.Count.ShouldBe(4);
        files[0].Status.ShouldBe("Modified");
        files[1].Status.ShouldBe("Untracked");
        files[1].Path.ShouldBe("b.txt");
        files[2].Status.ShouldBe("AM");
        files[3].Status.ShouldBe("Renamed");
        files[3].Path.ShouldBe("n.cs", "a seta -> resolve para o path novo");
    }

    [Fact]
    public void Dado_NumstatComRename_Quando_Parse_Entao_PathNovoEBinarioZero()
    {
        var map = GitDiffParser.ParseNumstatPerFile(
            "3\t1\tsrc/a.cs\n-\t-\timg.png\n2\t0\tdir/{old.cs => new.cs}\n");

        map.Count.ShouldBe(3);
        map["src/a.cs"].ShouldBe((3, 1));
        map["img.png"].ShouldBe((0, 0), "- - (binário) conta como 0");
        map["new.cs"].ShouldBe((2, 0));
    }

    [Fact]
    public void Dado_TextoSemNewlineFinal_Quando_CountLines_Entao_ContaUltima()
    {
        var path = Path.Combine(_workdir, "f.txt");
        File.WriteAllText(path, "a\nb\nlast");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);

        GitDiffParser.CountLinesInStream(stream).ShouldBe(3);
    }

    [Fact]
    public void Dado_BinarioComNul_Quando_CountLines_Entao_RetornaZero()
    {
        var path = Path.Combine(_workdir, "bin.dat");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x00, 0x00, 0x0A]);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);

        GitDiffParser.CountLinesInStream(stream).ShouldBe(0);
    }

    [Fact]
    public void Dado_DirColapsado_Quando_UntrackedInsertions_Entao_SomaDescendentes()
    {
        var untracked = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["newdir/a.txt"] = 3,
            ["newdir/sub/b.txt"] = 2,
            ["outro/c.txt"] = 9,
        };
        var file = new Taskboard.Dtos.WorkspaceDiffFileDto("newdir/", "Untracked");

        GitDiffParser.UntrackedInsertions(file, untracked).ShouldBe(5);
    }

    [Fact]
    public void Dado_ArquivoUntracked_Quando_UntrackedInsertions_Entao_LinhaDireta()
    {
        var untracked = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["n.txt"] = 7,
        };
        var file = new Taskboard.Dtos.WorkspaceDiffFileDto("n.txt", "Untracked");

        GitDiffParser.UntrackedInsertions(file, untracked).ShouldBe(7);
        GitDiffParser.UntrackedInsertions(
            new Taskboard.Dtos.WorkspaceDiffFileDto("m.txt", "Modified"), untracked).ShouldBe(0);
    }

    [Fact]
    public void Dado_ArquivoNoDisco_Quando_CountTextLines_Entao_ContaLinhas()
    {
        File.WriteAllLines(Path.Combine(_workdir, "c.txt"), ["x", "y", "z", "w"]);

        GitDiffParser.CountTextLines(_workdir, "c.txt").ShouldBe(4);
        GitDiffParser.CountTextLines(_workdir, "ausente.txt").ShouldBe(0);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workdir, recursive: true);
        }
        catch (IOException)
        {
            // Temp residue is harmless on the test host.
        }
    }
}
