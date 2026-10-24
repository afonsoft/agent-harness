using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261023 RF-B02/RF-B03: política off|sanitize|sanitize+wrap —
/// default seguro (sanitize+wrap), sentinel UNTRUSTED só no modo completo.
/// </summary>
public class PromptDefensePolicyTests
{
    private static PromptDefensePolicy FromConfig(string? value)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(value is null
                ? []
                : new Dictionary<string, string?> { ["Taskboard:Security:PromptDefense"] = value })
            .Build();
        return PromptDefensePolicy.FromConfiguration(cfg);
    }

    [Fact]
    public void Dado_SemConfig_Quando_FromConfiguration_Entao_DefaultSanitizeWrap()
        => FromConfig(null).Mode.ShouldBe(PromptDefenseMode.SanitizeWrap);

    [Theory]
    [InlineData("off", PromptDefenseMode.Off)]
    [InlineData("sanitize", PromptDefenseMode.Sanitize)]
    [InlineData("sanitize+wrap", PromptDefenseMode.SanitizeWrap)]
    [InlineData("SANITIZE+WRAP", PromptDefenseMode.SanitizeWrap)]
    [InlineData("bogus-value", PromptDefenseMode.SanitizeWrap)]
    public void Dado_ConfigValidaOuInvalida_Quando_FromConfiguration_Entao_ParseOuDefault(
        string value, PromptDefenseMode expected)
        => FromConfig(value).Mode.ShouldBe(expected);

    [Fact]
    public void Dado_ModoOff_Quando_Apply_Entao_PassaTextoSemAlteracao()
    {
        const string dirty = "a​b";
        FromConfig("off").Apply(dirty, "mailbox").ShouldBe(dirty);
    }

    [Fact]
    public void Dado_ModoSanitize_Quando_Apply_Entao_LimpaSemSentinela()
        => FromConfig("sanitize").Apply("a​b", "mailbox").ShouldBe("ab");

    [Fact]
    public void Dado_ModoSanitizeWrap_Quando_Apply_Entao_LimpaEEnvolveEmSentinela()
    {
        var result = FromConfig("sanitize+wrap").Apply("pay​load", "github_issue_comment");
        result.ShouldBe("<<<UNTRUSTED github_issue_comment>>>\npayload\n<<<END_UNTRUSTED>>>");
    }

    [Fact]
    public void Dado_Sanitize_Quando_InvocadoDireto_Entao_NuncaEnvolve()
    {
        FromConfig("sanitize+wrap").Sanitize("a​b").ShouldBe("ab");
        FromConfig("off").Sanitize("a​b").ShouldBe("a​b");
    }
}
