using Shouldly;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-security-permission-gateway RF-002 — path jail.</summary>
public class PathJailValidatorTests : IDisposable
{
    private readonly string _worktree;


    public PathJailValidatorTests()
    {
        _worktree = Path.Combine(Path.GetTempPath(), $"tb-jail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_worktree);
    }

    public void Dispose() => Directory.Delete(_worktree, recursive: true);

    [Fact]
    public void Dado_PathDentroDoWorktree_Quando_Validate_Entao_RetornaCanonico()
    {
        var result = PathJailValidator.Validate("src/Foo.cs", _worktree);

        result.ShouldBe(Path.GetFullPath(Path.Combine(_worktree, "src/Foo.cs")));
    }

    [Fact]
    public void Dado_PathAbsolutoDentro_Quando_Validate_Entao_Aceita()
        => PathJailValidator.Validate(Path.Join(_worktree, "a.txt"), _worktree)
            .ShouldBe(Path.GetFullPath(Path.Join(_worktree, "a.txt")));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/home/ubuntu/.ssh/authorized_keys")]
    [InlineData("../outside.txt")]
    [InlineData("../../root")]
    [InlineData("~/segredo")]
    [InlineData("$HOME/x")]
    public void Dado_PathForaDoWorktree_Quando_Validate_Entao_SecurityAccessDenied(string path)
        => Should.Throw<SecurityAccessDeniedException>(() => PathJailValidator.Validate(path, _worktree))
            .Code.ShouldBe(TaskboardDomainErrorCodes.SecurityAccessDenied);

    [Fact]
    public void Dado_SymlinkParaFora_Quando_Validate_Entao_SecurityAccessDenied()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"tb-outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "x");
        var link = Path.Combine(_worktree, "link.txt");
        File.CreateSymbolicLink(link, outside);

        Should.Throw<SecurityAccessDeniedException>(() => PathJailValidator.Validate("link.txt", _worktree));
    }

    [Fact]
    public void Dado_SymlinkParaDentro_Quando_Validate_Entao_Aceita()
    {
        var inside = Path.Combine(_worktree, "real.txt");
        File.WriteAllText(inside, "x");
        var link = Path.Join(_worktree, "alias.txt");
        File.CreateSymbolicLink(link, inside);

        PathJailValidator.Validate("alias.txt", _worktree).ShouldBe(inside);
    }

    [Fact]
    public void Dado_PathComTraversalDisfarcado_Quando_Validate_Entao_SecurityAccessDenied()
        => Should.Throw<SecurityAccessDeniedException>(
            () => PathJailValidator.Validate("src/../../etc/passwd", _worktree));

    [Fact]
    public void Dado_NovoArquivoInexistenteDentro_Quando_Validate_Entao_Aceita()
        => PathJailValidator.Validate("novo/dir/arquivo.txt", _worktree)
            .ShouldContain("novo");

    // SPEC-20261023-p0-agent-hardening RF-A01: credential-path dentro do
    // worktree (repo de dotfiles, workspace livre) é deny duro — nem Standard
    // nem aprovação humana tornam o acesso seguro.
    [Theory]
    [InlineData(".ssh/id_rsa")]
    [InlineData(".aws/credentials")]
    [InlineData(".gnupg/secring.gpg")]
    [InlineData(".kube/config")]
    [InlineData("dotfiles/.ssh/config")]
    [InlineData(".agent-harness/secrets.json")]
    public void Dado_CredentialPathDentroDoWorktree_Quando_Validate_Entao_SecurityAccessDenied(string path)
    {
        var ex = Should.Throw<SecurityAccessDeniedException>(() => PathJailValidator.Validate(path, _worktree));

        ex.Message.ShouldContain("credential-path");
    }

    [Fact]
    public void Dado_SymlinkDentroParaCredentialPath_Quando_Validate_Entao_SecurityAccessDenied()
    {
        var sshDir = Path.Combine(_worktree, ".ssh");
        Directory.CreateDirectory(sshDir);
        var real = Path.Combine(sshDir, "id_rsa");
        File.WriteAllText(real, "x");
        File.CreateSymbolicLink(Path.Combine(_worktree, "alias.txt"), real);

        Should.Throw<SecurityAccessDeniedException>(
            () => PathJailValidator.Validate("alias.txt", _worktree));
    }

    [Fact]
    public void Dado_DenylistExtraOperador_Quando_Validate_Entao_SecurityAccessDenied()
    {
        var denylist = new SensitivePathDenylist(["*/vault/*"]);

        Should.Throw<SecurityAccessDeniedException>(
            () => PathJailValidator.Validate("vault/token", _worktree, denylist));
    }
}
