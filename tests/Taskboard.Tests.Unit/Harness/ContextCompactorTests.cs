using Shouldly;
using Taskboard.Dtos;
using Taskboard.Integrations.Harness.Context;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class ContextCompactorTests
{
    private readonly ContextCompactor _sut = new();

    private static ContextMessageDto Msg(string role, string content)
        => new(role, content, ContextCompactor.EstimateTokens(content.Length));

    // AC-02: abaixo de 80% do budget → nenhuma compactação
    [Fact]
    public async Task Dado_HistoricoAbaixoDoLimite_Quando_CompactIfNeeded_Entao_Inalterado()
    {
        var messages = new List<ContextMessageDto>
        {
            Msg("system", "system prompt"),
            Msg("user", "faça X"),
            Msg("assistant", "feito"),
        };

        var result = await _sut.CompactIfNeededAsync(messages, maxTokens: 10000);

        result.StrategyApplied.ShouldBe("None");
        result.Messages.ShouldBe(messages);
        result.CompactedTokens.ShouldBe(result.OriginalTokens);
    }

    // AC-02: acima de 80% → reduz para <50% preservando system + última instrução
    [Fact]
    public async Task Dado_HistoricoAcimaDoLimite_Quando_CompactIfNeeded_Entao_ReduzPreservando()
    {
        var messages = new List<ContextMessageDto> { Msg("system", "system prompt") };
        for (var i = 0; i < 50; i++)
        {
            messages.Add(Msg("user", new string('u', 400)));
            messages.Add(Msg("assistant", new string('a', 2000)));
            messages.Add(Msg("tool", new string('t', 2000)));
        }

        messages.Add(Msg("user", "última instrução importante"));

        var originalTokens = messages.Sum(m => m.EstimatedTokens);
        var maxTokens = (int)(originalTokens / 0.85); // força ~85% de uso

        var result = await _sut.CompactIfNeededAsync(messages, maxTokens);

        result.CompactedTokens.ShouldBeLessThan((int)(maxTokens * 0.5));
        result.CompactedTokens.ShouldBeLessThan(result.OriginalTokens);
        result.Messages.ShouldContain(m => m.Role == "system" && m.Content == "system prompt");
        result.Messages.Last().Content.ShouldBe("última instrução importante");
        result.StrategyApplied.ShouldBe("Summarize");
        result.Messages.ShouldContain(m => m.Content.Contains("summary", StringComparison.OrdinalIgnoreCase)
            || m.Content.Contains("resumo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Dado_ApenasSystemEUser_Quando_CompactIfNeeded_Entao_NaoCompacta()
    {
        var messages = new List<ContextMessageDto>
        {
            Msg("system", new string('s', 400)),
            Msg("user", new string('u', 400)),
        };

        var result = await _sut.CompactIfNeededAsync(messages, maxTokens: 200);

        result.StrategyApplied.ShouldBe("None");
        result.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_TokensEstimadosZero_Quando_CompactIfNeeded_Entao_EstimaPorConteudo()
    {
        var messages = new List<ContextMessageDto>
        {
            new("system", "sys", 0),
            new("user", new string('x', 4000), 0),
            new("assistant", new string('y', 4000), 0),
            new("user", "final", 0),
        };

        var result = await _sut.CompactIfNeededAsync(messages, maxTokens: 2000);

        result.OriginalTokens.ShouldBeGreaterThan(0);
        result.Messages.Last().Content.ShouldBe("final");
    }
}
