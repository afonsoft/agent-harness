namespace Taskboard.Dtos;

/// <summary>A single conversation turn fed to the context compactor.</summary>
public sealed record ContextMessageDto(
    string Role,
    string Content,
    int EstimatedTokens);

/// <summary>
/// Result of <c>IContextCompactor.CompactIfNeededAsync</c>
/// (SPEC-20260919-harness-context-memory RF-003).
/// </summary>
public sealed record CompactionResultDto(
    IReadOnlyList<ContextMessageDto> Messages,
    int OriginalTokens,
    int CompactedTokens,
    string StrategyApplied);
