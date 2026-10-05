using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-001/RNF-004: estimador de
/// pressão — ceil(bytes/4) + 16/tool-call + 1000/imagem + anchor.
/// </summary>
public sealed class TokenPressureEstimatorTests
{
    [Fact]
    public void Dado_WireVazia_Quando_Estima_Entao_Zero()
    {
        TokenPressureEstimator.EstimateTokens([]).ShouldBe(0);
    }

    [Fact]
    public void Dado_Conteudo_Quando_Estima_Entao_BytesDivididosPorQuatroMaisFraming()
    {
        var wire = new List<OpenAiChatMessage> { new("user", new string('a', 100)) };

        var tokens = TokenPressureEstimator.EstimateTokens(wire);

        tokens.ShouldBe((100 + 3) / 4 + 4);
    }

    [Fact]
    public void Dado_ToolCalls_Quando_Estima_Entao_SomaOverheadEArgumentos()
    {
        var wire = new List<OpenAiChatMessage>
        {
            new("assistant", null,
                [
                    new OpenAiToolCall("1", "a", new string('x', 40)),
                    new OpenAiToolCall("2", "b", new string('x', 40)),
                ]),
        };

        var tokens = TokenPressureEstimator.EstimateTokens(wire);

        tokens.ShouldBe(4 + 2 * TokenPressureEstimator.ToolCallOverheadTokens + 2 * ((40 + 3) / 4));
    }

    [Fact]
    public void Dado_MarcadorDeImagem_Quando_Estima_Entao_MilTokensCada()
    {
        var wire = new List<OpenAiChatMessage>
        {
            new("user", "olha [image] e [image]"),
        };

        var tokens = TokenPressureEstimator.EstimateTokens(wire);

        tokens.ShouldBeGreaterThanOrEqualTo(2 * TokenPressureEstimator.ImageMarkerTokens);
    }

    [Fact]
    public void Dado_AnchorCobreTudo_Quando_Estima_Entao_RetornaAnchor()
    {
        var wire = new List<OpenAiChatMessage> { new("user", "x"), new("assistant", "y") };

        TokenPressureEstimator.AnchoredEstimate(wire, anchorTokens: 5000, anchorCount: 2).ShouldBe(5000);
    }

    [Fact]
    public void Dado_AnchorParcial_Quando_Estima_Entao_SomaDelta()
    {
        var wire = new List<OpenAiChatMessage>
        {
            new("user", "x"),
            new("user", new string('a', 400)),
        };

        var tokens = TokenPressureEstimator.AnchoredEstimate(wire, anchorTokens: 5000, anchorCount: 1);

        tokens.ShouldBe(5000 + (400 + 3) / 4 + 4);
    }

    [Fact]
    public void Dado_AnchorZero_Quando_Estima_Entao_EstimativaCompleta()
    {
        var wire = new List<OpenAiChatMessage> { new("user", new string('a', 100)) };

        TokenPressureEstimator.AnchoredEstimate(wire, anchorTokens: 0, anchorCount: 0)
            .ShouldBe(TokenPressureEstimator.EstimateTokens(wire));
    }
}
