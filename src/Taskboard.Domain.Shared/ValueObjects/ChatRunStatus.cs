namespace Taskboard.ValueObjects;

/// <summary>
/// Lifecycle of a provider-chat run (SPEC-20261005-chat-background-resume):
/// queued → running → completed|failed|stopped. Runs orphaned by a server
/// restart become <see cref="Interrupted"/>; a live run can also park as
/// <see cref="Paused"/> and resume later (SPEC-20261012-chat-run-controls).
/// </summary>
public sealed record ChatRunStatus : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "queued",
        "running",
        "paused",
        "completed",
        "failed",
        "stopped",
        "interrupted",
    };

    public static readonly ChatRunStatus Queued = new("queued");
    public static readonly ChatRunStatus Running = new("running");
    public static readonly ChatRunStatus Paused = new("paused");
    public static readonly ChatRunStatus Completed = new("completed");
    public static readonly ChatRunStatus Failed = new("failed");
    public static readonly ChatRunStatus Stopped = new("stopped");
    public static readonly ChatRunStatus Interrupted = new("interrupted");

    public ChatRunStatus(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatRunStatus From(string value) => new(value);

    /// <summary>True while the run still produces work (queued, in flight or parked).</summary>
    public bool IsActive => this == Queued || this == Running || this == Paused;

    /// <summary>True once the run reached a terminal state.</summary>
    public bool IsTerminal => !IsActive;
}
