using Shouldly;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20261023-p0-agent-hardening RF-A01/RF-A03 — denylist de credential-paths.</summary>
public class SensitivePathDenylistTests
{
    private readonly SensitivePathDenylist _sut = SensitivePathDenylist.Shared;

    [Theory]
    [InlineData("/home/ubuntu/.ssh/id_rsa")]
    [InlineData("/worktrees/dotfiles/.ssh/config")]
    [InlineData(".ssh/id_rsa")]                 // arg relativo — match por segmento
    [InlineData("./.ssh/config")]
    [InlineData("~/.ssh/id_rsa")]
    [InlineData(".ssh")]                        // o diretório em si
    [InlineData(".ssh/")]
    public void Dado_PathSsh_Quando_TryMatch_Entao_ClasseSshDir(string path)
    {
        _sut.TryMatch(path, out var cls).ShouldBeTrue();
        cls.ShouldBe("ssh-dir");
    }

    [Theory]
    [InlineData("/home/u/.aws/credentials", "aws-credentials")]
    [InlineData(".aws/config", "aws-credentials")]
    [InlineData("/x/.gnupg/secring.gpg", "gpg-dir")]
    [InlineData(".gnupg", "gpg-dir")]
    [InlineData("/x/.azure/accessTokens.json", "azure-credentials")]
    [InlineData(".azure", "azure-credentials")]
    [InlineData("/x/.config/gcloud/credentials", "gcp-credentials")]
    [InlineData("/x/.docker/config.json", "docker-config")]
    [InlineData("/x/.kube/config", "kube-config")]
    [InlineData("/x/.agent-harness/secrets.json", "harness-secrets")]
    [InlineData("/x/.agent-harness/credentials.db", "harness-secrets")]
    [InlineData(".agent-harness/secrets", "harness-secrets")]
    public void Dado_PathCredencial_Quando_TryMatch_Entao_ClasseEsperada(string path, string expected)
    {
        _sut.TryMatch(path, out var cls).ShouldBeTrue();
        cls.ShouldBe(expected);
    }

    [Theory]
    [InlineData("/repo/src/Foo.cs")]
    [InlineData("/repo/.ssh-config")]            // semelhante, mas não é o dir .ssh
    [InlineData("/repo/.aws/credentials.json")]  // só credentials/config exatos negam
    [InlineData("/repo/.aws/sdk/cache")]
    [InlineData("/repo/.config/app.json")]       // .config sozinho não é gcloud
    [InlineData("/repo/.docker/Dockerfile")]
    [InlineData("/repo/.agent-harness/sessions/x.jsonl")]
    [InlineData("/repo/.agent-harness/attachments/img.png")]
    [InlineData("src/README.md")]
    public void Dado_PathBenigno_Quando_TryMatch_Entao_NaoNegado(string path)
        => _sut.TryMatch(path, out _).ShouldBeFalse();

    [Fact]
    public void Dado_PatternExtraOperador_Quando_TryMatch_Entao_ClasseCustom()
    {
        var sut = new SensitivePathDenylist(["*/vault/*"]);

        sut.TryMatch("/data/vault/token", out var cls).ShouldBeTrue();
        cls.ShouldBe("custom");
    }

    [Fact]
    public void Dado_PatternExtraOperador_Quando_Builtin_Entao_BuiltinsMantidos()
    {
        // Append-only: extras nunca removem os padrões embutidos.
        var sut = new SensitivePathDenylist(["*/vault/*"]);

        sut.TryMatch("/x/.ssh/id_rsa", out _).ShouldBeTrue();
    }
}
