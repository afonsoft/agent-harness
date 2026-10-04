using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20261001-chat-default-mode FR-001: política de modo padrão —
/// chat quando configurado (provider > CLI chat), senão agent com hint.
/// </summary>
public class DefaultModePolicyTests
{
    [Fact]
    public void Dado_ModoAuto_Quando_ProviderChatConfigurado_Entao_DefaultChat()
    {
        var r = DefaultModePolicy.Resolve("auto", providerChatAvailable: true, cliChatAvailable: false);
        r.Mode.ShouldBe("provider");
        r.FellBack.ShouldBeFalse();
    }

    [Fact]
    public void Dado_ModoAuto_Quando_SoCliChatCapable_Entao_DefaultAgent()
    {
        // SPEC-20261003-ai-code-agent-chat: o modo assistant foi removido —
        // Agent É a superfície de chat com delegação para CLIs.
        var r = DefaultModePolicy.Resolve("auto", providerChatAvailable: false, cliChatAvailable: true);
        r.Mode.ShouldBe("agent");
        r.FellBack.ShouldBeFalse();
    }

    [Fact]
    public void Dado_ModoAuto_Quando_SemCanalDeChat_Entao_DefaultAgentComHint()
    {
        var r = DefaultModePolicy.Resolve("auto", providerChatAvailable: false, cliChatAvailable: false);
        r.Mode.ShouldBe("agent");
        r.FellBack.ShouldBeTrue();
    }

    [Fact]
    public void Dado_ModoChat_Quando_ChatIndisponivel_Entao_FallbackAgent()
    {
        var r = DefaultModePolicy.Resolve("chat", providerChatAvailable: false, cliChatAvailable: false);
        r.Mode.ShouldBe("agent");
        r.FellBack.ShouldBeTrue();
    }

    [Fact]
    public void Dado_ModoChat_Quando_SoCliCapable_Entao_AgentSemFallback()
    {
        var r = DefaultModePolicy.Resolve("chat", providerChatAvailable: false, cliChatAvailable: true);
        r.Mode.ShouldBe("agent");
        r.FellBack.ShouldBeFalse();
    }

    [Fact]
    public void Dado_ModoAgent_Quando_ChatDisponivel_Entao_DefaultAgent()
    {
        var r = DefaultModePolicy.Resolve("agent", providerChatAvailable: true, cliChatAvailable: true);
        r.Mode.ShouldBe("agent");
        r.FellBack.ShouldBeFalse();
    }

    [Fact]
    public void Dado_ConfigInvalida_Quando_Resolve_Entao_ComportaComoAuto()
    {
        var r = DefaultModePolicy.Resolve("banana", providerChatAvailable: true, cliChatAvailable: false);
        r.Mode.ShouldBe("provider");
    }

    [Fact]
    public void Dado_ConfigNull_Quando_Resolve_Entao_ComportaComoAuto()
    {
        var r = DefaultModePolicy.Resolve(null, providerChatAvailable: false, cliChatAvailable: true);
        r.Mode.ShouldBe("agent");
    }
}
