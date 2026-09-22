using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260922-ai-chat-command-bar — helpers testáveis do command bar:
/// <see cref="AiChatThreadTitle"/> (RF-007, auto-título do primeiro prompt) e
/// <see cref="ProblemDetailReader"/> (RF-003, motivo real do erro).
/// </summary>
public class AiChatThreadTitleTests
{
    [Fact]
    public void Dado_PromptVazio_Quando_Derive_Entao_TituloDefault()
    {
        AiChatThreadTitle.Derive("").ShouldBe("New conversation");
        AiChatThreadTitle.Derive("   \n\t  ").ShouldBe("New conversation");
    }

    [Fact]
    public void Dado_PromptCurto_Quando_Derive_Entao_NormalizaWhitespace()
    {
        AiChatThreadTitle.Derive("  fix   the\n\tlogin   bug  ").ShouldBe("fix the login bug");
    }

    [Fact]
    public void Dado_PromptLongo_Quando_Derive_Entao_TruncaComEllipsis()
    {
        var prompt = string.Concat(Enumerable.Repeat("palavra ", 20));

        var title = AiChatThreadTitle.Derive(prompt);

        title.ShouldEndWith("…");
        title.Length.ShouldBeLessThanOrEqualTo(AiChatThreadTitle.MaxLength + 1);
    }

    [Fact]
    public void Dado_PromptLongo_Quando_Derive_Entao_CortaEmFronteiraDePalavra()
    {
        // 60+ chars com um espaço além de 1/3 do limite → corte na palavra.
        var prompt = new string('a', 30) + " " + new string('b', 40);

        var title = AiChatThreadTitle.Derive(prompt);

        title.ShouldBe(new string('a', 30) + "…");
    }

    [Fact]
    public void Dado_PromptSemEspaco_Quando_Derive_Entao_TruncaHard()
    {
        var prompt = new string('x', 200);

        var title = AiChatThreadTitle.Derive(prompt);

        title.Length.ShouldBe(AiChatThreadTitle.MaxLength + 1);
        title.ShouldEndWith("…");
    }
}

public class ProblemDetailReaderTests
{
    [Fact]
    public void Dado_ProblemJsonComDetail_Quando_TryRead_Entao_RetornaDetail()
    {
        const string body = """{"type":"about:blank","title":"Error","status":400,"detail":"Repository 'owner/ghost' has no local workspace.","code":"VALIDATION_ERROR"}""";

        ProblemDetailReader.TryRead(body).ShouldBe("Repository 'owner/ghost' has no local workspace.");
    }

    [Fact]
    public void Dado_ProblemJsonSemDetail_Quando_TryRead_Entao_RetornaCode()
    {
        const string body = """{"status":422,"code":"AGENT_NOT_ELIGIBLE"}""";

        ProblemDetailReader.TryRead(body).ShouldBe("AGENT_NOT_ELIGIBLE");
    }

    [Fact]
    public void Dado_EnvelopeError_Quando_TryRead_Entao_RetornaMessage()
    {
        const string body = """{"error":{"code":"THREAD_NOT_FOUND","message":"Thread 'abc' not found."}}""";

        ProblemDetailReader.TryRead(body).ShouldBe("Thread 'abc' not found.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    public void Dado_CorpoInvalido_Quando_TryRead_Entao_RetornaNull(string? body)
    {
        ProblemDetailReader.TryRead(body).ShouldBeNull();
    }

    [Fact]
    public void Dado_JsonSemCamposConhecidos_Quando_TryRead_Entao_RetornaNull()
    {
        ProblemDetailReader.TryRead("""{"title":"Error","status":500}""").ShouldBeNull();
    }
}
