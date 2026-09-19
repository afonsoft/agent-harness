namespace Taskboard.GitHub;

/// <summary>
/// Workflow do GitHub Actions de um repositório, com o run mais recente
/// embutido (SPEC-20260918-workflow-github-actions).
/// </summary>
public sealed record WorkflowDto(
    long Id,
    string Name,
    string Path,
    string State,
    string HtmlUrl,
    WorkflowRunDto? LastRun);
