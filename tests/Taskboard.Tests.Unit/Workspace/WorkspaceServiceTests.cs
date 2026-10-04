using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Workspace;
using Xunit;

namespace Taskboard.Tests.Unit.Workspace;

/// <summary>SPEC-20261004 RF-001/RF-002: workspace dir listing + path normalization.</summary>
public class WorkspaceServiceTests : IDisposable
{
    private readonly string _home;
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _home = Directory.CreateTempSubdirectory("taskboard-home-").FullName;
        _service = new WorkspaceService(null, _home, NullLogger<WorkspaceService>.Instance);
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string Root => Path.Combine(_home, "repos");

    [Fact]
    public void Dado_SubdirsNoRoot_Quando_Listar_Entao_EntradasOrdenadasEPai()
    {
        Directory.CreateDirectory(Path.Combine(Root, "beta"));
        Directory.CreateDirectory(Path.Combine(Root, "alpha"));
        Directory.CreateDirectory(Path.Combine(Root, ".hidden"));
        File.WriteAllText(Path.Combine(Root, "file.txt"), "x");

        var listing = _service.ListSubdirs(null)!;

        listing.Path.ShouldBe(Root);
        listing.Entries.Select(e => e.Name).ShouldBe(["alpha", "beta"]);
        listing.Entries[0].Path.ShouldBe(Path.Combine(Root, "alpha"));
        listing.Parent.ShouldBe(_home);
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void Dado_PathRelativo_Quando_Listar_Entao_ResolveSobRoot()
    {
        Directory.CreateDirectory(Path.Combine(Root, "proj", "src"));

        var listing = _service.ListSubdirs("proj")!;

        listing.Path.ShouldBe(Path.Combine(Root, "proj"));
        listing.Parent.ShouldBe(Root);
        listing.Entries.Select(e => e.Name).ShouldBe(["src"]);
    }

    [Fact]
    public void Dado_PathForaDeHome_Quando_Listar_Entao_Null()
    {
        _service.ListSubdirs("/etc").ShouldBeNull();
        _service.ListSubdirs("~/../../etc").ShouldBeNull();
        _service.ListSubdirs("../../etc").ShouldBeNull();
    }

    [Fact]
    public void Dado_PathQueNaoEDir_Quando_Listar_Entao_Null()
    {
        var file = Path.Combine(Root, "f.txt");
        Directory.CreateDirectory(Root);
        File.WriteAllText(file, "x");

        _service.ListSubdirs("f.txt").ShouldBeNull();
    }

    [Fact]
    public void Dado_TildeERelativo_Quando_Normalizar_Entao_ResolucoesCorretas()
    {
        _service.NormalizeWorkspacePath(null).ShouldBeNull();
        _service.NormalizeWorkspacePath("  ").ShouldBeNull();
        _service.NormalizeWorkspacePath("~").ShouldBe(_home);
        _service.NormalizeWorkspacePath("~/repos/proj").ShouldBe(Path.Combine(_home, "repos", "proj"));
        _service.NormalizeWorkspacePath("proj").ShouldBe(Path.Combine(_home, "repos", "proj"));
        _service.NormalizeWorkspacePath("/etc").ShouldBeNull();
        _service.NormalizeWorkspacePath("~/../outside").ShouldBeNull();
    }

    [Fact]
    public void Dado_SymlinkParaForaDeHome_Quando_Normalizar_Entao_Null()
    {
        var outside = Directory.CreateTempSubdirectory("taskboard-outside-");
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateSymbolicLink(Path.Combine(Root, "link"), outside.FullName);

            _service.NormalizeWorkspacePath("link").ShouldBeNull();
            _service.ListSubdirs("link").ShouldBeNull();
        }
        finally
        {
            Directory.Delete(outside.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dado_SymlinkDentroDeHome_Quando_Normalizar_Entao_ResolveAlvo()
    {
        Directory.CreateDirectory(Root);
        var real = Path.Combine(_home, "realdir");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(Path.Combine(Root, "link"), real);

        _service.NormalizeWorkspacePath("link").ShouldBe(real);
    }
}
