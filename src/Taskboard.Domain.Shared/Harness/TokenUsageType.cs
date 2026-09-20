namespace Taskboard.Harness;

/// <summary>
/// Classification of token usage reported by an agent CLI/API
/// (SPEC-20260919-ade-observability-finops RF-001).
/// </summary>
public enum TokenUsageType
{
    Input,
    Output,
    CacheWrite,
    CacheRead
}
