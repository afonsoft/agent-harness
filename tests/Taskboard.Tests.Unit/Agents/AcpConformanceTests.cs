using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Domain.Entities;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.Repositories;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v1-conformance: handshake capabilities (RF-001/004),
/// client-side fs/terminal sandboxing (RF-008/009) e gating de métodos
/// opcionais por capability.
/// </summary>
public sealed class AcpConformanceTests
{
    [Fact]
    public void Dado_InitializeCompleto_Quando_FromInitialize_Entao_CapturaCapabilitiesEAuth()
    {
        var json = """
        {
            "protocolVersion": 1,
            "agentInfo": { "name": "claude-agent-acp", "version": "0.5.0", "title": "Claude" },
            "agentCapabilities": {
                "loadSession": true,
                "promptCapabilities": { "image": true, "audio": false, "embeddedContext": true },
                "mcpCapabilities": { "http": true, "sse": false },
                "sessionCapabilities": {
                    "resume": {}, "close": {}, "delete": {}, "list": {}, "additionalDirectories": {}
                },
                "auth": { "logout": {} }
            },
            "authMethods": [
                { "id": "oauth", "type": "agent", "name": "OAuth", "description": "Browser login" },
                { "id": "cli", "type": "terminal", "name": "CLI login", "args": ["claude", "login"] }
            ]
        }
        """;

        var peer = AcpPeerInfo.FromInitialize(JsonDocument.Parse(json).RootElement);

        peer.ProtocolVersion.ShouldBe(1);
        peer.AgentName.ShouldBe("claude-agent-acp");
        peer.AgentVersion.ShouldBe("0.5.0");
        peer.LoadSession.ShouldBeTrue();
        peer.SessionResume.ShouldBeTrue();
        peer.SessionClose.ShouldBeTrue();
        peer.SessionDelete.ShouldBeTrue();
        peer.SessionList.ShouldBeTrue();
        peer.AdditionalDirectories.ShouldBeTrue();
        peer.McpHttp.ShouldBeTrue();
        peer.McpSse.ShouldBeFalse();
        peer.PromptImage.ShouldBeTrue();
        peer.PromptAudio.ShouldBeFalse();
        peer.PromptEmbeddedContext.ShouldBeTrue();
        peer.AuthLogout.ShouldBeTrue();
        peer.AuthMethods.Count.ShouldBe(2);
        peer.AuthMethods[0].Id.ShouldBe("oauth");
        peer.AuthMethods[1].Type.ShouldBe("terminal");
        peer.AuthMethods[1].Args.ShouldBe(["claude", "login"]);
    }

    [Fact]
    public void Dado_InitializeMinimo_Quando_FromInitialize_Entao_CapabilitiesAusentesFalsas()
    {
        var json = """{ "protocolVersion": 1, "agentCapabilities": {}, "authMethods": [] }""";

        var peer = AcpPeerInfo.FromInitialize(JsonDocument.Parse(json).RootElement);

        peer.ProtocolVersion.ShouldBe(1);
        peer.LoadSession.ShouldBeFalse();
        peer.SessionResume.ShouldBeFalse();
        peer.SessionClose.ShouldBeFalse();
        peer.McpHttp.ShouldBeFalse();
        peer.AuthMethods.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SessionResultComModesEConfig_Quando_ApplySessionResult_Entao_Captura()
    {
        var peer = new AcpPeerInfo();
        var json = """
        {
            "sessionId": "s-1",
            "modes": { "currentModeId": "build", "availableModes": [{ "id": "build" }, { "id": "plan" }] },
            "configOptions": [
                { "id": "model", "category": "model", "value": "claude-sonnet",
                  "options": [{ "value": "claude-sonnet" }, { "value": "claude-opus" }] }
            ]
        }
        """;

        peer.ApplySessionResult(JsonDocument.Parse(json).RootElement);

        peer.Modes.ShouldNotBeNull();
        peer.Modes!.Value.GetProperty("currentModeId").GetString().ShouldBe("build");
        peer.ConfigOptions.ShouldNotBeNull();
        peer.ConfigOptions!.Value.GetArrayLength().ShouldBe(1);
        peer.ConfigOptions.Value[0].GetProperty("id").GetString().ShouldBe("model");
    }

    [Fact]
    public async Task Dado_FsReadDentroDoWorkspace_Quando_HandleAsync_Entao_RetornaConteudo()
    {
        var (handler, root) = CriarHandler(out _);
        var file = Path.Combine(root, "src", "hello.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "linha1\nlinha2\nlinha3");

        var p = JsonDocument.Parse($$"""{"path":"{{file}}"}""").RootElement;
        var result = await handler.HandleAsync("t1", "s1", "fs/read_text_file", p, CancellationToken.None);

        result.GetProperty("content").GetString().ShouldBe("linha1\nlinha2\nlinha3");
    }

    [Fact]
    public async Task Dado_FsReadComLineELimit_Quando_HandleAsync_Entao_RetornaFatia()
    {
        var (handler, root) = CriarHandler(out _);
        var file = Path.Combine(root, "big.txt");
        await File.WriteAllTextAsync(file, "l1\nl2\nl3\nl4\nl5");

        var p = JsonDocument.Parse($$"""{"path":"{{file}}","line":2,"limit":2}""").RootElement;
        var result = await handler.HandleAsync("t1", "s1", "fs/read_text_file", p, CancellationToken.None);

        result.GetProperty("content").GetString().ShouldBe("l2\nl3");
    }

    [Fact]
    public async Task Dado_PathForaDoWorkspace_Quando_FsRead_Entao_InvalidParams()
    {
        var (handler, _) = CriarHandler(out _);
        var p = JsonDocument.Parse("""{"path":"/etc/hostname"}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "fs/read_text_file", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task Dado_PathRelativo_Quando_FsRead_Entao_InvalidParams()
    {
        var (handler, _) = CriarHandler(out _);
        var p = JsonDocument.Parse("""{"path":"./relative.txt"}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "fs/read_text_file", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task Dado_EscapeComDotDot_Quando_FsRead_Entao_InvalidParams()
    {
        var (handler, root) = CriarHandler(out _);
        var escaped = Path.GetFullPath(Path.Combine(root, "..", "..", "secret.txt"));
        var p = JsonDocument.Parse($$"""{"path":"{{escaped.Replace("\\", "\\\\")}}"}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "fs/read_text_file", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task Dado_FsWriteAprovado_Quando_HandleAsync_Entao_EscreveArquivo()
    {
        var (handler, root) = CriarHandler(out var gate, responder: "allow");
        var file = Path.Combine(root, "novo.txt");
        var p = JsonDocument.Parse(
            $$"""{"path":"{{file.Replace("\\", "\\\\")}}","content":"conteudo"}""").RootElement;

        await handler.HandleAsync("t1", "s1", "fs/write_text_file", p, CancellationToken.None);

        (await File.ReadAllTextAsync(file)).ShouldBe("conteudo");
        gate.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_FsWriteNegado_Quando_HandleAsync_Entao_RequestCancelled()
    {
        var (handler, root) = CriarHandler(out _, responder: "deny");
        var file = Path.Combine(root, "negado.txt");
        var p = JsonDocument.Parse(
            $$"""{"path":"{{file.Replace("\\", "\\\\")}}","content":"x"}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "fs/write_text_file", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.RequestCancelled);
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_TerminalDesabilitado_Quando_TerminalCreate_Entao_MethodNotFound()
    {
        var (handler, _) = CriarHandler(out _); // ClientTerminal default = false
        var p = JsonDocument.Parse("""{"command":"echo","args":["hi"]}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "terminal/create", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.MethodNotFound);
    }

    [Fact]
    public async Task Dado_MetodoDesconhecido_Quando_HandleAsync_Entao_MethodNotFound()
    {
        var (handler, _) = CriarHandler(out _);
        var p = JsonDocument.Parse("""{}""").RootElement;

        var ex = await Should.ThrowAsync<AcpException>(
            () => handler.HandleAsync("t1", "s1", "elicitation/select", p, CancellationToken.None));
        ex.Code.ShouldBe(AcpErrorCode.MethodNotFound);
    }

    private static (AcpClientToolHandler Handler, string Root) CriarHandler(
        out PermissionGate gate, string responder = "deny")
    {
        var root = Path.Combine(Path.GetTempPath(), $"acp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        // Auto-responder: the gate publishes the pending request via SSE; the
        // stub stream answers it immediately with the configured outcome.
        var stream = new AutoReplyStream();
        gate = new PermissionGate(stream);
        stream.Gate = gate;
        stream.Outcome = responder;

        var workspace = new WorkspaceService(root, root, NullLogger<WorkspaceService>.Instance);
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "t", ModelRef.From("default"), "medium",
            Sandbox.WorkspaceWrite, AgentType.OpenCode, workspacePath: root);
        var threadRepo = Substitute.For<IRepository<AiChatThread>>();
        threadRepo.GetAsync(Arg.Any<AiChatThreadId>(), Arg.Any<CancellationToken>())
            .Returns(thread);

        var services = new ServiceCollection();
        services.AddScoped(_ => threadRepo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var handler = new AcpClientToolHandler(
            scopeFactory, workspace, gate, new AcpSessionOptions(),
            NullLogger<AcpClientToolHandler>.Instance);
        return (handler, root);
    }

    private sealed class AutoReplyStream : IThreadEventStreamService
    {
        public PermissionGate? Gate { get; set; }
        public string Outcome { get; set; } = "deny";

        public Task PublishAsync(string threadId, ServerSentEvent serverSentEvent, CancellationToken ct = default)
        {
            if (serverSentEvent.Payload is PermissionRequestInfo info)
            {
                Gate?.Reply(threadId, info.RequestId, Outcome);
            }
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ServerSentEvent> SubscribeAsync(string threadId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
