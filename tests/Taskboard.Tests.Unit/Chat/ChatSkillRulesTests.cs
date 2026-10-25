using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261025 C3 — skills pins + environment kill list.</summary>
public sealed class ChatSkillRulesTests
{
    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    [Fact]
    public void Dado_Pins_Quando_OrderCatalog_Entao_PinsPrimeiroNaOrdem()
    {
        var config = Config(new() { [ChatSkillRules.PinnedKey] = """["zzz","mid"]""" });
        var skills = new[] { "alpha", "zzz", "mid", "beta" };

        ChatSkillRules.OrderCatalog(skills, config, s => s)
            .ShouldBe(["zzz", "mid", "alpha", "beta"]);
    }

    [Fact]
    public void Dado_SemPins_Quando_OrderCatalog_Entao_Alfabetico()
    {
        var config = Config();
        var skills = new[] { "zzz", "alpha", "mid" };

        ChatSkillRules.OrderCatalog(skills, config, s => s)
            .ShouldBe(["alpha", "mid", "zzz"]);
    }

    [Fact]
    public void Dado_PinCap_Quando_Pinned_Entao_TruncaEmCinco()
    {
        var config = Config(new()
        {
            [ChatSkillRules.PinnedKey] = """["a","b","c","d","e","f","g"]""",
        });

        ChatSkillRules.Pinned(config).ShouldBe(["a", "b", "c", "d", "e"]);
    }

    [Fact]
    public void Dado_KillList_Quando_IsEnabled_Entao_SkillBloqueada()
    {
        var config = Config(new() { [ChatSkillRules.DisabledKey] = """["rogue-skill"]""" });

        ChatCapabilityRules.IsEnabled(config, "skill:rogue-skill").ShouldBeFalse();
        ChatCapabilityRules.IsEnabled(config, "skill:fine-skill").ShouldBeTrue();
        ChatCapabilityRules.IsEnabled(config, "tool:shell_exec").ShouldBeTrue();
    }

    [Fact]
    public void Dado_KillListCaseInsensitive_Quando_IsEnabled_Entao_Bloqueia()
    {
        var config = Config(new() { [ChatSkillRules.DisabledKey] = """["Rogue-Skill"]""" });

        ChatCapabilityRules.IsEnabled(config, "skill:rogue-skill").ShouldBeFalse();
    }

    [Fact]
    public void Dado_JsonInvalido_Quando_Regras_Entao_FalhaSegura()
    {
        var config = Config(new()
        {
            [ChatSkillRules.PinnedKey] = "{not-json",
            [ChatSkillRules.DisabledKey] = "42",
        });

        ChatSkillRules.Pinned(config).ShouldBeEmpty();
        ChatSkillRules.Disabled(config).ShouldBeEmpty();
        ChatCapabilityRules.IsEnabled(config, "skill:anything").ShouldBeTrue();
    }
}
