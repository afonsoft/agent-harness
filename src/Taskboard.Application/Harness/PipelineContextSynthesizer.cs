using System.Text;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;

namespace Taskboard.Application.Harness;

/// <summary>
/// Builds the dispatched prompt for a pipeline stage and condenses each
/// completed stage's output into the upstream handoff
/// (SPEC-20260919-ade-multi-agent-orchestration RF-004).
/// </summary>
public static class PipelineContextSynthesizer
{
    /// <summary>Cap per-stage handoff so upstream context cannot blow up the prompt.</summary>
    public const int MaxHandoffChars = 4000;

    public static string BuildStagePrompt(PipelineExecution execution, PipelineStageExecution stage)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("# Task");
        prompt.AppendLine(execution.InitialPrompt);
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(execution.WorktreePath))
        {
            prompt.AppendLine("# Workspace");
            prompt.AppendLine($"Isolated git worktree: `{execution.WorktreePath}`");
            prompt.AppendLine($"Repository: {execution.RepositoryFullName} (base `{execution.BaseBranch}`)");
            prompt.AppendLine("All file changes MUST stay inside the worktree.");
            prompt.AppendLine();
        }

        var upstream = execution.Stages
            .Where(s => s.HandoffSummary is not null)
            .ToList();
        if (upstream.Count > 0)
        {
            prompt.AppendLine("# Upstream context");
            foreach (var up in upstream)
            {
                prompt.AppendLine($"## {up.Name} ({up.Role?.ToString() ?? up.Kind.ToString()})");
                prompt.AppendLine(up.HandoffSummary);
                prompt.AppendLine();
            }
        }

        prompt.AppendLine("# Your role");
        prompt.AppendLine(stage.Role switch
        {
            AgentRole.Architect =>
                "You are the Architect. Produce a concrete implementation plan/spec for the task. Do NOT write production code.",
            AgentRole.Builder =>
                "You are the Builder. Implement the task in the worktree, following any upstream plan.",
            AgentRole.Tester =>
                "You are the Tester. Author automated tests covering the task in the worktree.",
            AgentRole.Reviewer =>
                "You are the Reviewer. Inspect the worktree diff for correctness, conventions and security. Report findings; fix only trivial issues.",
            _ => "Execute the stage instructions.",
        });
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(stage.AdjustedPrompt))
        {
            prompt.AppendLine("# Adjustment (retry)");
            prompt.AppendLine(stage.AdjustedPrompt);
            prompt.AppendLine();
        }

        return prompt.ToString();
    }

    /// <summary>Tail-truncated output kept as the stage's handoff (RF-004).</summary>
    public static string SummarizeOutput(IEnumerable<string> outputChunks)
    {
        var joined = string.Join('\n', outputChunks.Where(c => !string.IsNullOrWhiteSpace(c)));
        return joined.Length <= MaxHandoffChars
            ? joined
            : "…" + joined[^MaxHandoffChars..];
    }
}
