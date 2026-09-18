namespace Taskboard.Requests;

/// <summary>
/// Saves the default agent prompt template. <c>null</c> or empty clears the
/// override, restoring the builtin template.
/// </summary>
public sealed record PromptTemplateRequest(string? Template);
