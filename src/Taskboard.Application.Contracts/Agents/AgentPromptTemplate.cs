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

        {issueComments}
        """;

    /// <summary>
    /// Returns true when the template carries the <c>{issueComments}</c>
    /// placeholder (older overrides may not).
    /// </summary>
    public static bool HasCommentsPlaceholder(string template) =>
        template.Contains("{issueComments}", StringComparison.Ordinal);

    /// <summary>Substitutes <c>{repoUrl}</c>, <c>{issueTitle}</c>, <c>{issueBody}</c> and <c>{issueComments}</c>.</summary>
    public static string Render(
        string template, string? repoUrl, string? issueTitle, string? issueBody, string? issueComments = null) =>
        template
            .Replace("{repoUrl}", repoUrl ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueTitle}", issueTitle ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueBody}", issueBody ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueComments}", issueComments ?? string.Empty, StringComparison.Ordinal)
            .Trim();
}
