namespace Taskboard.ValueObjects;

/// <summary>
/// Lifecycle of a chat background job (SPEC-20261005-chat-jobs-schedule-search
/// RF-001): queued → running → finished|killed|terminated. <c>terminated</c>
/// covers rows left "running" by a dead host — the orphaned OS process is not
/// adoptable after a restart, so the boot sweep writes this state.
/// </summary>
public sealed record ChatJobStatus : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "queued",
        "running",
        "finished",
        "killed",
        "terminated",
    };

    public static readonly ChatJobStatus Queued = new("queued");
    public static readonly ChatJobStatus Running = new("running");
    public static readonly ChatJobStatus Finished = new("finished");
    public static readonly ChatJobStatus Killed = new("killed");
    public static readonly ChatJobStatus Terminated = new("terminated");

    public ChatJobStatus(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatJobStatus From(string value) => new(value);

    /// <summary>True while a process may still be producing output.</summary>
    public bool IsActive => this == Queued || this == Running;

    /// <summary>True once the row reached a terminal state.</summary>
    public bool IsTerminal => !IsActive;
}
