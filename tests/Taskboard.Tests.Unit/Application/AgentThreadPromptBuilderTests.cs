using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Application;

public class AgentThreadPromptBuilderTests
{
    private static AiChatEventDto Event(string role, string content) =>
        new(Guid.NewGuid().ToString("N"), "thread-1", role, content, DateTime.UtcNow);

    [Fact]
    public void Dado_ThreadComEventos_Quando_Build_Entao_IncluiTituloRepoEConversa()
    {
        var events = new List<AiChatEventDto>
        {
            Event("user", "qual o estado do backlog?"),
            Event("assistant", "temos 3 issues abertas")
        };

        var prompt = AgentThreadPromptBuilder.Build("Sprint review", "owner/repo", events);

        prompt.ShouldContain("owner/repo");
        prompt.ShouldContain("Sprint review");
        prompt.ShouldContain("user: qual o estado do backlog?");
        prompt.ShouldContain("assistant: temos 3 issues abertas");
    }

    [Fact]
    public void Dado_MaisEventosQueOLimite_Quando_Build_Entao_MantemOsMaisRecentes()
    {
        var events = Enumerable.Range(1, 25)
            .Select(i => Event("user", $"mensagem {i}"))
            .ToList();

        var prompt = AgentThreadPromptBuilder.Build("t", "o/r", events, maxMessages: 20);

        prompt.ShouldNotContain("mensagem 1\n");
        prompt.ShouldNotContain("mensagem 5\n");
        prompt.ShouldContain("mensagem 6");
        prompt.ShouldContain("mensagem 25");
    }

    [Fact]
    public void Dado_ConteudoMaiorQueCap_Quando_Build_Entao_TruncaEmMaxChars()
    {
        var events = new List<AiChatEventDto> { Event("user", new string('x', 20000)) };

        var prompt = AgentThreadPromptBuilder.Build("t", "o/r", events, maxChars: 500);

        prompt.Length.ShouldBe(500);
    }

    [Fact]
    public void Dado_ConversaLonga_Quando_Truncar_Entao_MensagemRecenteEFechamentoSobrevivem()
    {
        var events = new List<AiChatEventDto>
        {
            Event("user", new string('x', 20000)),
            Event("user", "qual o status?")
        };

        var prompt = AgentThreadPromptBuilder.Build("t", "o/r", events, maxChars: 2000);

        prompt.ShouldContain("user: qual o status?");
        prompt.ShouldContain("Continue this work");
        prompt.ShouldEndWith("report what you did.");
        prompt.Length.ShouldBeLessThanOrEqualTo(2000);
    }

    [Fact]
    public void Dado_RolesDesconhecidos_Quando_Build_Entao_NormalizaParaSystem()
    {
        var events = new List<AiChatEventDto> { Event("activity", "agente enfileirado") };

        var prompt = AgentThreadPromptBuilder.Build("t", "o/r", events);

        prompt.ShouldContain("system: agente enfileirado");
    }

    [Fact]
    public void Dado_ThreadSemEventos_Quando_Build_Entao_RetornaHeaderEInstrucao()
    {
        var prompt = AgentThreadPromptBuilder.Build("vazia", "owner/repo", []);

        prompt.ShouldContain("owner/repo");
        prompt.ShouldContain("<conversation>");
        prompt.ShouldContain("Continue this work");
    }
}
