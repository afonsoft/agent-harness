using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Default agent prompt template and placeholder substitution
/// (SPEC-20260918-agent-execution-ux RF-004). The runtime override lives at
/// <c>Taskboard:Agents:DefaultPrompt</c>; this builtin is the fallback.
/// </summary>
public static class AgentPromptTemplate
{
    public const string ConfigurationKey = "Taskboard:Agents:DefaultPrompt";

    public const int MaxLength = 8192;

    public const string Builtin = """
        Use the orchestrator skill to manage the entire process for this task: plan the work, delegate to the specialized skills, and validate the result before finishing.
        Clone the repository {repoUrl} into the current working directory (~/repos) and apply the fixes described in the issue below.
        Use the available skills to optimize the process, consult the "knowledge" MCP for additional context when needed, and use the manage-taskboard skill to move and update the issue card.

        Issue: {issueTitle}

        {issueBody}
        """;

    /// <summary>Substitutes <c>{repoUrl}</c>, <c>{issueTitle}</c> and <c>{issueBody}</c>.</summary>
    public static string Render(string template, string? repoUrl, string? issueTitle, string? issueBody) =>
        template
            .Replace("{repoUrl}", repoUrl ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueTitle}", issueTitle ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueBody}", issueBody ?? string.Empty, StringComparison.Ordinal)
            .Trim();
}
