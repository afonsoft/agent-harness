using Taskboard.Domain.Entities;
using Taskboard.Dtos;

namespace Taskboard.Application.Mapping;

public static class DomainMappingExtensions
{
    public static AiChatThreadDto ToDto(this AiChatThread thread)
        => new(
            thread.Id.Value,
            thread.Title,
            thread.Model.Value,
            thread.ReasoningEffort,
            thread.Sandbox.Value,
            thread.Status.Value,
            thread.CreatedAt,
            thread.UpdatedAt,
            thread.Version,
            thread.Mode,
            thread.AgentType?.ToString(),
            thread.WorkspacePath,
            thread.RepositoryFullName,
            thread.ModelTier,
            thread.ModelSource)
        {
            // SPEC-20260928-ai-code-generic-cli RF-003: "pty" renders the
            // terminal pane; everything else stays the structured chat.
            Kind = string.Equals(thread.Transport, "pty", StringComparison.OrdinalIgnoreCase)
                ? "terminal"
                : "chat",
            Transport = thread.Transport,
            ContainerContext = thread.ContainerContext,
            AgentCliId = thread.AgentCliId,
        };

    public static AiChatRunDto ToDto(this AiChatRun run)
        => new(
            run.Id.Value,
            run.ThreadId.Value,
            run.Status,
            run.ExitCode,
            run.CreatedAt,
            run.FinishedAt);

    public static AiChatEventDto ToDto(this AiChatEvent chatEvent)
        => new(
            chatEvent.Id.Value,
            chatEvent.ThreadId.Value,
            chatEvent.Role.Value,
            chatEvent.Content,
            chatEvent.CreatedAt,
            chatEvent.Kind.Value,
            chatEvent.PayloadJson);
}