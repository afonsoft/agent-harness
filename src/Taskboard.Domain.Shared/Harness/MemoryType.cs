namespace Taskboard.Harness;

/// <summary>
/// Kind of a persisted cross-session memory (SPEC-20260919-harness-context-memory RF-004).
/// </summary>
public enum MemoryType
{
    Fact,
    ArchitecturalDecision,
    LessonLearned
}
