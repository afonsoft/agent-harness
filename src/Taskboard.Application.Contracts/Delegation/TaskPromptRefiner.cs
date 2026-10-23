namespace Taskboard.Application.Contracts.Delegation;

/// <summary>SPEC-20261022-task-prompt-refiner RF-001: input to prompt
/// refinement — the raw prompt plus the context needed to assemble the
/// self-contained worker brief.</summary>
public sealed record TaskPromptRefineRequest(
    string RawPrompt,
    string? Cli,
    string? RepositoryPath,
    string? Workdir,
    string? IssueRef,
    string? PriorFailureDetail,
    int Attempt);

/// <summary>Refinement outcome — <see cref="Refined"/> is what the worker
/// receives; <see cref="Mode"/> records how it was produced.</summary>
public sealed record TaskPromptRefineResult(
    string Raw,
    string Refined,
    string Mode,
    IReadOnlyList<string> SpecAnchors,
    IReadOnlyList<string> AppliedRules)
{
    public static TaskPromptRefineResult Off(string raw) =>
        new(raw, raw, "off", [], []);
}

/// <summary>Refinement seam (RF-001). Pure service — no side effects besides
/// the optional LLM call in <c>llm</c> mode.</summary>
public interface ITaskPromptRefiner
{
    Task<TaskPromptRefineResult> RefineAsync(
        TaskPromptRefineRequest request, CancellationToken ct);
}

public static class TaskPromptRefinementModes
{
    public const string Off = "off";
    public const string Template = "template";
    public const string Llm = "llm";
    public const string LlmFallback = "llm-fallback";

    public const string ConfigurationKey = "Taskboard:Delegation:PromptRefinement";
    public const string Default = Template;

    public static bool IsValid(string? value) =>
        value is Off or Template or Llm or LlmFallback;
}