namespace Taskboard.GitHub;

/// <summary>
/// A single <c>labeled</c>/<c>unlabeled</c> event from a GitHub issue's
/// timeline — the raw material used to reconstruct column transitions
/// (SPEC-20260918-gantt-github-timeline).
/// </summary>
public sealed record IssueLabelEventDto(
    DateTimeOffset At,
    string Label,
    bool Added);
