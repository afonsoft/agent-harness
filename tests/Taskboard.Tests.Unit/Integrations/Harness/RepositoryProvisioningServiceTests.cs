using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Integrations.Harness;
using Taskboard.Integrations.Workspace;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Harness;

/// <summary>
/// SPEC-20260923-cockpit-run-hardening RF-001 — the clone path is always
/// <c>&lt;root&gt;/&lt;name&gt;</c>; existing dirs are reused only when the
/// origin remote matches the requested repo.
/// </summary>
public class RepositoryProvisioningServiceTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "tb-prov-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceService _workspace;
    private readonly IGitCommandRunner _git = Substitute.For<IGitCommandRunner>();

    public RepositoryProvisioningServiceTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = new WorkspaceService(_root, _root, NullLogger<WorkspaceService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RepositoryProvisioningService Criar() =>
        new(_git, _workspace, NullLogger<RepositoryProvisioningService>.Instance);

    private string CloneDir(string name = "agent-harness") => Path.Join(_root, name);

    [Fact]
    public void Dado_OwnerName_Quando_ResolveClonePath_Entao_PathSobRoot()
    {
        Criar().ResolveClonePath("afonsoft/agent-harness").ShouldBe(CloneDir());
    }

    [Fact]
    public void Dado_UrlCompleta_Quando_ResolveClonePath_Entao_UltimoSegmento()
    {
        Criar().ResolveClonePath("https://github.com/afonsoft/agent-harness")
            .ShouldBe(CloneDir());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("owner/")]
    public void Dado_NomeInvalido_Quando_ResolveClonePath_Entao_InvalidValue(string repo)
    {
        Should.Throw<DomainException>(() => Criar().ResolveClonePath(repo))
            .Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_DirInexistente_Quando_EnsureClone_Entao_GitCloneParaPath()
    {
        _git.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "", "", false));
        var path = CloneDir();

        var result = await Criar().EnsureCloneAsync("afonsoft/agent-harness");

        result.Path.ShouldBe(path);
        result.Cloned.ShouldBeTrue();
        await _git.Received(1).RunAsync(
            _root,
            Arg.Is<IReadOnlyList<string>>(a =>
                a[0] == "clone"
                && a[1] == "https://github.com/afonsoft/agent-harness.git"
                && a[2] == path),
            Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DirVazio_Quando_EnsureClone_Entao_ClonaEmVezDeReutilizar()
    {
        Directory.CreateDirectory(CloneDir());
        _git.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "", "", false));

        var result = await Criar().EnsureCloneAsync("afonsoft/agent-harness");

        result.Cloned.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://github.com/afonsoft/agent-harness")]
    [InlineData("https://github.com/afonsoft/agent-harness.git")]
    [InlineData("git@github.com:afonsoft/agent-harness.git")]
    [InlineData("ssh://git@github.com/afonsoft/agent-harness")]
    [InlineData("https://github.com/AFONSOFT/AGENT-HARNESS.git")]
    public async Task Dado_CloneExistente_Quando_RemoteConfere_Entao_ReutilizaSemClonar(string remoteUrl)
    {
        var dir = CloneDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "marker.txt"), "x");
        _git.RunAsync(dir, Arg.Is<IReadOnlyList<string>>(a => a[0] == "rev-parse"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "true\n", "", false));
        _git.RunAsync(dir, Arg.Is<IReadOnlyList<string>>(a => a[0] == "remote"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, remoteUrl + "\n", "", false));

        var result = await Criar().EnsureCloneAsync("afonsoft/agent-harness");

        result.Cloned.ShouldBeFalse();
        result.Path.ShouldBe(dir);
        await _git.DidNotReceive().RunAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => a[0] == "clone"),
            Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DirNaoRepositorio_Quando_EnsureClone_Entao_ProvisioningFailed()
    {
        var dir = CloneDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "arquivo.txt"), "x");
        _git.RunAsync(dir, Arg.Is<IReadOnlyList<string>>(a => a[0] == "rev-parse"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(128, "", "not a git repository", false));

        var ex = await Should.ThrowAsync<DomainException>(
            () => Criar().EnsureCloneAsync("afonsoft/agent-harness"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.RepositoryProvisioningFailed);
        ex.Message.ShouldContain("not a git repository");
    }

    [Fact]
    public async Task Dado_RepoEstrangeiro_Quando_EnsureClone_Entao_RecusaSemApagar()
    {
        // Regressão do incidente: ~/repos é um clone de LangGraph-UI — reutilizar
        // um clone de outro repo foi o bug do pipe_0bfefc2f.
        var dir = CloneDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "marker.txt"), "x");
        _git.RunAsync(dir, Arg.Is<IReadOnlyList<string>>(a => a[0] == "rev-parse"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "true\n", "", false));
        _git.RunAsync(dir, Arg.Is<IReadOnlyList<string>>(a => a[0] == "remote"),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "https://github.com/afonsoft/LangGraph-UI.git\n", "", false));

        var ex = await Should.ThrowAsync<DomainException>(
            () => Criar().EnsureCloneAsync("afonsoft/agent-harness"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.RepositoryProvisioningFailed);
        ex.Message.ShouldContain("LangGraph-UI");
        File.Exists(Path.Join(dir, "marker.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_CloneFalha_Quando_GitRetornaErro_Entao_ProvisioningFailed()
    {
        _git.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(128, "", "repository not found", false));

        var ex = await Should.ThrowAsync<DomainException>(
            () => Criar().EnsureCloneAsync("afonsoft/repo-privado-inexistente"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.RepositoryProvisioningFailed);
        ex.Message.ShouldContain("repository not found");
    }

    [Fact]
    public async Task Dado_CloneTimeout_Quando_GitEstoura_Entao_ProvisioningFailed()
    {
        _git.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(-1, "", "", TimedOut: true));

        var ex = await Should.ThrowAsync<DomainException>(
            () => Criar().EnsureCloneAsync("afonsoft/agent-harness"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.RepositoryProvisioningFailed);
        ex.Message.ShouldContain("timeout");
    }
}
