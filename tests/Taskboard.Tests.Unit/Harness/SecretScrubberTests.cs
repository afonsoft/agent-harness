using Shouldly;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-security-permission-gateway RF-003 — secret scrubbing.</summary>
public class SecretScrubberTests
{
    private readonly SecretScrubber _sut = new();

    [Theory]
    [InlineData("token: ghp_A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8")]
    [InlineData("gho_A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8 rest")]
    [InlineData("github_pat_11ABCDEFG0_aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789aBcDeFgHiJkLmNoP")]
    [InlineData("sk-aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789aBcDeFgHiJkLm")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    public void Dado_OutputComSegredo_Quando_Scrub_Entao_Redacted(string output)
    {
        var result = _sut.Scrub(output);

        result.ShouldContain("[REDACTED_SECRET]");
        result.ShouldNotBe(output);
    }

    [Fact]
    public void Dado_OutputComSegredo_Quando_Scrub_Entao_ValorOriginalNaoAparece()
    {
        const string secret = "ghp_A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8";

        var result = _sut.Scrub($"GITHUB_TOKEN={secret} ok");

        result.ShouldNotContain(secret);
    }

    [Fact]
    public void Dado_OutputLimpo_Quando_Scrub_Entao_Inalterado()
    {
        const string clean = "Build succeeded. 0 Warning(s) 0 Error(s)";

        _sut.Scrub(clean).ShouldBe(clean);
    }

    [Fact]
    public void Dado_VariosSegredos_Quando_Scrub_Entao_TodosRedacted()
    {
        var result = _sut.Scrub(
            "a ghp_A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8 b AKIAIOSFODNN7EXAMPLE c");

        result.ShouldNotContain("ghp_");
        result.ShouldNotContain("AKIA");
    }

    [Fact]
    public void Dado_NuloOuVazio_Quando_Scrub_Entao_RetornaEntrada()
    {
        _sut.Scrub(string.Empty).ShouldBe(string.Empty);
    }
}
