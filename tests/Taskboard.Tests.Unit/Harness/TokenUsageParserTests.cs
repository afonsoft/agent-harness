using Shouldly;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public sealed class TokenUsageParserTests
{
    [Fact]
    public void Dado_JsonResultClaude_Quando_Extrai_Entao_TokensComCache()
    {
        var line = """{"type":"result","subtype":"success","usage":{"input_tokens":31200,"output_tokens":7200,"cache_creation_input_tokens":500,"cache_read_input_tokens":1800}}""";

        var usage = TokenUsageParser.TryExtract(line);

        usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(31200);
        usage.OutputTokens.ShouldBe(7200);
        usage.CacheWriteTokens.ShouldBe(500);
        usage.CacheReadTokens.ShouldBe(1800);
    }

    [Fact]
    public void Dado_EventoTokenCountCodex_Quando_Extrai_Entao_TotalTokenUsage()
    {
        var line = """{"msg":{"type":"token_count","info":{"total_token_usage":{"input_tokens":9000,"cached_input_tokens":1000,"output_tokens":2000,"reasoning_output_tokens":300,"total_tokens":12000}}}}""";

        var usage = TokenUsageParser.TryExtract(line);

        usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(9000);
        usage.OutputTokens.ShouldBe(2000);
        usage.CacheReadTokens.ShouldBe(1000);
    }

    [Fact]
    public void Dado_UsageOpenAiPromptCompletion_Quando_Extrai_Entao_MapaCampos()
    {
        var line = """{"usage":{"prompt_tokens":1500,"completion_tokens":400,"total_tokens":1900}}""";

        var usage = TokenUsageParser.TryExtract(line);

        usage.ShouldNotBeNull();
        usage.InputTokens.ShouldBe(1500);
        usage.OutputTokens.ShouldBe(400);
    }

    [Fact]
    public void Dado_LinhaTextoLivre_Quando_Extrai_Entao_Nulo()
    {
        TokenUsageParser.TryExtract("agente trabalhando na issue #169...").ShouldBeNull();
    }

    [Fact]
    public void Dado_JsonSemUsage_Quando_Extrai_Entao_Nulo()
    {
        TokenUsageParser.TryExtract("""{"type":"assistant","message":"ok"}""").ShouldBeNull();
    }

    [Fact]
    public void Dado_JsonInvalido_Quando_Extrai_Entao_Nulo()
    {
        TokenUsageParser.TryExtract("""{"usage": {"input_tokens": ""}}""").ShouldBeNull();
    }
}
