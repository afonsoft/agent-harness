using System.Text.Json;
using Shouldly;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-201/RF-202: dialetos v1/v2 isolam o wire
/// format — builders de params, nomes de método e flags por versão.
/// </summary>
public sealed class AcpDialectTests
{
    private static readonly AcpSessionOptions Options = new()
    {
        ClientFs = true,
        ClientTerminal = false,
        TerminalAuth = true,
        BooleanConfigOptions = true
    };

    [Fact]
    public void Dado_DialetoV1_Quando_BuildInitializeParams_Entao_ShapeV1()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV1Dialect.Instance.BuildInitializeParams(Options));

        p.GetProperty("protocolVersion").GetInt32().ShouldBe(1);
        p.GetProperty("clientCapabilities").GetProperty("fs").GetProperty("readTextFile").GetBoolean().ShouldBeTrue();
        p.GetProperty("clientInfo").GetProperty("name").GetString().ShouldBe("taskboard");
    }

    [Fact]
    public void Dado_DialetoV2_Quando_BuildInitializeParams_Entao_ShapeV2SemClientCaps()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV2Dialect.Instance.BuildInitializeParams(Options));

        p.GetProperty("protocolVersion").GetInt32().ShouldBe(2);
        p.GetProperty("info").GetProperty("name").GetString().ShouldBe("taskboard");
        p.TryGetProperty("capabilities", out _).ShouldBeTrue();
        // v2 removeu as capacidades client-side fs/terminal — não podem vazar.
        p.TryGetProperty("clientCapabilities", out _).ShouldBeFalse();
        p.TryGetProperty("clientInfo", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_DialetoV1ComAdditionalDirs_Quando_SessionNew_Entao_CampoPresente()
    {
        var peer = new AcpPeerInfo { AdditionalDirectories = true };

        var p = JsonSerializer.SerializeToElement(
            AcpV1Dialect.Instance.BuildSessionNewParams(peer, "/ws", [new { name = "m" }]));

        p.GetProperty("cwd").GetString().ShouldBe("/ws");
        p.GetProperty("mcpServers").GetArrayLength().ShouldBe(1);
        p.TryGetProperty("additionalDirectories", out _).ShouldBeTrue();
    }

    [Fact]
    public void Dado_DialetoV2SemMcp_Quando_SessionNew_Entao_OmiteMcpServers()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV2Dialect.Instance.BuildSessionNewParams(new AcpPeerInfo(), "/ws", []));

        p.GetProperty("cwd").GetString().ShouldBe("/ws");
        // mcpServers é opcional em v2 — vazio não é enviado.
        p.TryGetProperty("mcpServers", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_DialetoV2_Quando_ResumeComReplayFromStart_Entao_CursorPresente()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV2Dialect.Instance.BuildResumeParams("s-1", "/ws", [], replayFromStart: true));

        p.GetProperty("sessionId").GetString().ShouldBe("s-1");
        p.GetProperty("replayFrom").GetProperty("type").GetString().ShouldBe("start");
    }

    [Fact]
    public void Dado_DialetoV2_Quando_ResumeSemReplay_Entao_SemCursor()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV2Dialect.Instance.BuildResumeParams("s-1", "/ws", []));

        p.TryGetProperty("replayFrom", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_Dialetos_Quando_Flags_Entao_V1LeaksNaoAparecemEmV2()
    {
        var v1 = AcpV1Dialect.Instance;
        var v2 = AcpV2Dialect.Instance;

        v1.AuthenticateMethod.ShouldBe("authenticate");
        v2.AuthenticateMethod.ShouldBe("auth/login");

        v1.SupportsSessionLoad.ShouldBeTrue();
        v2.SupportsSessionLoad.ShouldBeFalse();

        v1.SupportsSetMode.ShouldBeTrue();
        v2.SupportsSetMode.ShouldBeFalse();

        v1.SupportsClientTools.ShouldBeTrue();
        v2.SupportsClientTools.ShouldBeFalse();

        v1.PromptResponseEndsTurn.ShouldBeTrue();
        v2.PromptResponseEndsTurn.ShouldBeFalse();
    }

    [Fact]
    public void Dado_VersaoDesconhecida_Quando_AcpDialectsFor_Entao_NotSupported()
    {
        Should.Throw<NotSupportedException>(() => AcpDialects.For(3));
        AcpDialects.IsSupported(1).ShouldBeTrue();
        AcpDialects.IsSupported(2).ShouldBeTrue();
        AcpDialects.IsSupported(0).ShouldBeFalse();
        AcpDialects.IsSupported(3).ShouldBeFalse();
    }

    [Fact]
    public void Dado_DialetoV1_Quando_BuildPromptParams_Entao_ContentBlocks()
    {
        var p = JsonSerializer.SerializeToElement(
            AcpV1Dialect.Instance.BuildPromptParams("s-1", "hello", "queue"));

        p.GetProperty("sessionId").GetString().ShouldBe("s-1");
        p.GetProperty("prompt")[0].GetProperty("type").GetString().ShouldBe("text");
        p.GetProperty("prompt")[0].GetProperty("text").GetString().ShouldBe("hello");
    }

    [Fact]
    public void Dado_PeerV2Completo_Quando_FromInitialize_Entao_BaselineECapabilities()
    {
        var json = """
        {
            "protocolVersion": 2,
            "info": { "name": "acp2-agent", "version": "2.0.0" },
            "capabilities": {
                "session": {
                    "delete": {},
                    "additionalDirectories": {},
                    "prompt": { "image": {}, "embeddedContext": {} },
                    "mcp": { "stdio": {}, "http": {} }
                }
            },
            "authMethods": [
                { "methodId": "oauth", "type": "agent", "name": "OAuth" }
            ]
        }
        """;

        var peer = AcpPeerInfo.FromInitialize(JsonDocument.Parse(json).RootElement, 2);

        peer.ProtocolVersion.ShouldBe(2);
        peer.AgentName.ShouldBe("acp2-agent");
        // capabilities.session implica o baseline de métodos.
        peer.SessionResume.ShouldBeTrue();
        peer.SessionClose.ShouldBeTrue();
        peer.SessionList.ShouldBeTrue();
        peer.SessionDelete.ShouldBeTrue();
        peer.AdditionalDirectories.ShouldBeTrue();
        peer.PromptImage.ShouldBeTrue();
        peer.PromptAudio.ShouldBeFalse();
        peer.McpHttp.ShouldBeTrue();
        peer.McpStdio.ShouldBeTrue();
        // authMethods não-vazio implica auth/login+logout — sem marcador.
        peer.AuthLogout.ShouldBeTrue();
        peer.AuthMethods.Count.ShouldBe(1);
        peer.AuthMethods[0].Id.ShouldBe("oauth");
        // session/load não existe em v2.
        peer.LoadSession.ShouldBeFalse();
    }

    [Fact]
    public void Dado_PeerV2SemAuthMethods_Quando_FromInitialize_Entao_LogoutIndisponivel()
    {
        var json = """{ "protocolVersion": 2, "info": {}, "capabilities": { "session": {} } }""";

        var peer = AcpPeerInfo.FromInitialize(JsonDocument.Parse(json).RootElement, 2);

        peer.AuthLogout.ShouldBeFalse();
        peer.AuthMethods.ShouldBeEmpty();
        peer.SessionResume.ShouldBeTrue();
    }
}
