namespace Taskboard.Dtos;

/// <summary>
/// Result of <c>IContextCompiler.CompileAsync</c> — the assembled system
/// prompt plus provenance metadata (SPEC-20260919-harness-context-memory §5).
/// </summary>
public sealed record ContextCompilationDto(
    string SystemPrompt,
    int EstimatedTokens,
    IReadOnlyList<string> InjectedFiles,
    int MemoriesInjectedCount);
