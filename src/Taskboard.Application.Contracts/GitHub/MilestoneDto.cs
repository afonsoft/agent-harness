namespace Taskboard.GitHub;

/// <summary>GitHub milestone — its <c>due_on</c> is the deadline marker on the Gantt.</summary>
public sealed record MilestoneDto(
    int Number,
    string Title,
    DateTimeOffset? DueOn,
    string State);
