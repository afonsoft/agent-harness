using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>Effective default prompt template plus its builtin fallback.</summary>
public sealed record AgentPromptTemplateInfo(string Template, string Builtin, bool Customized);

/// <summary>
/// Reads and updates the shared default agent prompt template
/// (SPEC-20260918-agent-execution-ux RF-004).
/// </summary>
public interface IAgentPromptTemplateService
{
    /// <summary>Returns the effective template (override or builtin).</summary>
    Task<AgentPromptTemplateInfo> GetTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the template; null/empty restores the builtin.</summary>
    Task SetTemplateAsync(string? template, CancellationToken cancellationToken = default);
}
