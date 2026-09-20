using Shouldly;
using Taskboard.Harness;
using Xunit;
namespace Taskboard.Tests.Unit.Harness;
public sealed class TokenCostCalculatorTests
{
    private static readonly IReadOnlyList<ModelPriceRateInfo> Rates =
    [
        new("anthropic", "claude-3-7-sonnet", 3.00m, 15.00m, 3.75m, 0.30m),
        new("anthropic", "claude-haiku-4", 1.00m, 5.00m, 1.25m, 0.10m),
        new("openai", "gpt-5", 1.25m, 10.00m, 1.25m, 0.125m),
        new("*", "*", 3.00m, 15.00m, 3.75m, 0.30m)
    ];
    [Fact]
    public void Dado_10kInput2kOutputClaude37_Quando_Calcula_Entao_CustoExatoPelaTaxa()
    {
        // AC1: 10k input * $3.00/1M + 2k output * $15.00/1M = $0.030 + $0.030 = $0.060
        var usage = new TokenUsage(10_000, 2_000, 0, 0);
        var cost = TokenCostCalculator.Calculate(Rates, "claude-3-7-sonnet", usage);
        cost.ShouldBe(0.06m);
    }
    [Fact]
    public void Dado_UsoComCache_Quando_Calcula_Entao_SomaQuatroComponentes()
    {
        // 1M input + 1M output + 1M cache-write + 1M cache-read em haiku:
        // 1.00 + 5.00 + 1.25 + 0.10 = 7.35
        var usage = new TokenUsage(1_000_000, 1_000_000, 1_000_000, 1_000_000);
        var cost = TokenCostCalculator.Calculate(Rates, "claude-haiku-4-5-20251001", usage);
        cost.ShouldBe(7.35m);
    }
    [Fact]
    public void Dado_ModeloComSufixoDeData_Quando_Calcula_Entao_MatchPorPrefixo()
    {
        var usage = new TokenUsage(1_000_000, 0, 0, 0);
        var cost = TokenCostCalculator.Calculate(Rates, "gpt-5.1-2026-01-01", usage);
        // prefixo "gpt-5" casa com "gpt-5.1-..."
        cost.ShouldBe(1.25m);
    }
    [Fact]
    public void Dado_ModeloDesconhecido_Quando_Calcula_Entao_UsaFallbackWildcard()
    {
        var usage = new TokenUsage(1_000_000, 0, 0, 0);
        var cost = TokenCostCalculator.Calculate(Rates, "modelo-inexistente-xyz", usage);
        cost.ShouldBe(3.00m);
    }
    [Fact]
    public void Dado_ModeloNulo_Quando_Calcula_Entao_UsaFallbackWildcard()
    {
        var cost = TokenCostCalculator.Calculate(Rates, null, new TokenUsage(1_000_000, 0, 0, 0));
        cost.ShouldBe(3.00m);
    }
    [Fact]
    public void Dado_SemWildcard_Quando_ModeloDesconhecido_Entao_CustoZero()
    {
        var rates = new[] { new ModelPriceRateInfo("anthropic", "claude-3-7-sonnet", 3m, 15m, 3.75m, 0.30m) };
        var cost = TokenCostCalculator.Calculate(rates, "desconhecido", new TokenUsage(1_000_000, 0, 0, 0));
        cost.ShouldBe(0m);
    }
    [Fact]
    public void Dado_ZeroTokens_Quando_Calcula_Entao_CustoZero()
    {
        TokenCostCalculator.Calculate(Rates, "claude-3-7-sonnet", TokenUsage.Zero).ShouldBe(0m);
    }
}
