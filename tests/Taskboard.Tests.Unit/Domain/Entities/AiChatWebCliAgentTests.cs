using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Entities;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Domain.Entities;

public sealed class AiChatWebCliAgentTests
{
    [Fact]
    public void Dado_KindValido_Quando_Criado_Entao_ValorCorreto()
    {
        // Covers RF-004: Eventos tipados
        var kind = AiChatEventKind.ToolCall;
        kind.Value.ShouldBe("tool_call");
        AiChatEventKind.From("reasoning").ShouldBe(AiChatEventKind.Reasoning);
        AiChatEventKind.From("message").ShouldBe(AiChatEventKind.Message);
        AiChatEventKind.From("permission").ShouldBe(AiChatEventKind.Permission);
    }

    [Fact]
    public void Dado_KindInvalido_Quando_Criado_Entao_LancaExcecao()
    {
        Should.Throw<DomainException>(() => AiChatEventKind.From("invalido"));
    }

    [Fact]
    public void Dado_ThreadEmModoAgent_Quando_Criada_Entao_PropriedadesConfiguradas()
    {
        // Covers RF-001: Thread em modo agent
        var id = AiChatThreadId.NewGuid();
        var model = ModelRef.From("claude-3-7-sonnet");
        var sandbox = Sandbox.WorkspaceWrite;

        var thread = AiChatThread.CreateAgentThread(
            id,
            "Interactive Agent Thread",
            model,
            "medium",
            sandbox,
            AgentType.OpenCode,
            workspacePath: "/home/ubuntu/repos/taskboard-ai",
            repositoryFullName: "afonsoft/taskboard-ai");

        thread.Mode.ShouldBe("agent");
        thread.AgentType.ShouldBe(AgentType.OpenCode);
        thread.WorkspacePath.ShouldBe("/home/ubuntu/repos/taskboard-ai");
        thread.RepositoryFullName.ShouldBe("afonsoft/taskboard-ai");
    }

    [Fact]
    public void Dado_ThreadEmModoAssistant_Quando_Criada_Entao_DefaultsPreservados()
    {
        // Covers RF-001: Retrocompatibilidade com modo assistant
        var id = AiChatThreadId.NewGuid();
        var model = ModelRef.From("claude-3-7-sonnet");
        var sandbox = Sandbox.WorkspaceWrite;

        var thread = AiChatThread.Create(
            id,
            "Standard Assistant Thread",
            model,
            "medium",
            sandbox);

        thread.Mode.ShouldBe("assistant");
        thread.AgentType.ShouldBeNull();
        thread.WorkspacePath.ShouldBeNull();
        thread.RepositoryFullName.ShouldBeNull();
    }

    [Fact]
    public void Dado_EventoComKindEPayload_Quando_Criado_Entao_PropriedadesConfiguradas()
    {
        // Covers RF-004: Eventos tipados com PayloadJson
        var threadId = AiChatThreadId.NewGuid();
        var eventId = AiChatEventId.NewGuid();

        var evt = AiChatEvent.CreateTyped(
            eventId,
            threadId,
            AiChatEventRole.Assistant,
            "Executando comando bash",
            AiChatEventKind.ToolCall,
            payloadJson: "{\"tool\":\"bash\",\"args\":[\"ls -la\"]}");

        evt.Kind.ShouldBe(AiChatEventKind.ToolCall);
        evt.PayloadJson.ShouldBe("{\"tool\":\"bash\",\"args\":[\"ls -la\"]}");
    }

    [Fact]
    public void Dado_AgentSessionCapability_Quando_Instanciado_Entao_PreservaValores()
    {
        // Covers RF-008: Capability detection
        var capability = new AgentSessionCapability(
            SupportsInteractiveSession: true,
            SupportsSteer: true,
            SupportsInlinePermissions: true);

        capability.SupportsInteractiveSession.ShouldBeTrue();
        capability.SupportsSteer.ShouldBeTrue();
        capability.SupportsInlinePermissions.ShouldBeTrue();
    }

    [Fact]
    public void Dado_AiChatThreadDto_Quando_Mapeado_Entao_ContemCamposAgent()
    {
        // Covers DTO mapping
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(),
            "Agent Thread DTO",
            ModelRef.From("gpt-5.6-luna"),
            "high",
            Sandbox.DangerFullAccess,
            AgentType.Codex,
            "/path/to/workdir",
            "org/repo");

        var dto = Taskboard.Application.Mapping.DomainMappingExtensions.ToDto(thread);

        dto.Mode.ShouldBe("agent");
        dto.AgentType.ShouldBe("Codex");
        dto.WorkspacePath.ShouldBe("/path/to/workdir");
        dto.RepositoryFullName.ShouldBe("org/repo");
    }

    [Fact]
    public void Dado_AiChatEventDto_Quando_Mapeado_Entao_ContemKindEPayload()
    {
        // Covers DTO mapping
        var evt = AiChatEvent.CreateTyped(
            AiChatEventId.NewGuid(),
            AiChatThreadId.NewGuid(),
            AiChatEventRole.Assistant,
            "Tool completed",
            AiChatEventKind.ToolCall,
            "{\"status\":\"ok\"}");

        var dto = Taskboard.Application.Mapping.DomainMappingExtensions.ToDto(evt);

        dto.Kind.ShouldBe("tool_call");
        dto.PayloadJson.ShouldBe("{\"status\":\"ok\"}");
    }
}
