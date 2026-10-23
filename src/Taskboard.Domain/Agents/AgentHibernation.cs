namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-001: lifecycle record of a hibernatable
/// session — carries the pin (RF-009) for live sessions and the resume
/// payload once killed (IsHibernated).
/// </summary>
public sealed class AgentHibernation : Entity<string>
{
    private AgentHibernation(string id)
        : base(id)
    {
    }

    public string SessionKey => Id;
    public string UserId { get; private set; } = "local";
    public string Cli { get; private set; } = string.Empty;
    /// <summary>True while the process is dead and the record holds the respawn data.</summary>
    public bool IsHibernated { get; private set; }
    public DateTime HibernatedAtUtc { get; private set; }
    public DateTime IdleAtUtc { get; private set; }
    /// <summary>Binary + args (JSON array) used to respawn the session.</summary>
    public string ResumeArgvJson { get; private set; } = "[]";
    /// <summary>The CLI's own session id — exact join onto the sessions index row.</summary>
    public string? NativeSessionId { get; private set; }
    public string? Cwd { get; private set; }
    public int AutoResumedCount { get; private set; }
    /// <summary>SPEC-20261022-mailbox-wake RF-002: last auto-resume ATTEMPT —
    /// the ≥60s storm throttle; attempts count, not successes.</summary>
    public DateTime? LastAutoResumeUtc { get; private set; }
    /// <summary>RF-009: user pin — hibernate never touches pinned sessions.</summary>
    public bool Pinned { get; private set; }

    public static AgentHibernation Create(
        string sessionKey, string userId, string cli, string resumeArgvJson,
        string? nativeSessionId, string? cwd, DateTime idleAtUtc, DateTime atUtc) =>
        new(sessionKey)
        {
            UserId = userId,
            Cli = cli,
            IsHibernated = true,
            HibernatedAtUtc = atUtc,
            IdleAtUtc = idleAtUtc,
            ResumeArgvJson = resumeArgvJson,
            NativeSessionId = nativeSessionId,
            Cwd = cwd,
        };

    /// <summary>RF-009: pin marker for a live session — not hibernated.</summary>
    public static AgentHibernation PinOnly(
        string sessionKey, string userId, string cli, string? nativeSessionId,
        string? cwd, DateTime atUtc) =>
        new(sessionKey)
        {
            UserId = userId,
            Cli = cli,
            IsHibernated = false,
            HibernatedAtUtc = atUtc,
            IdleAtUtc = atUtc,
            NativeSessionId = nativeSessionId,
            Cwd = cwd,
            Pinned = true,
        };

    /// <summary>Re-hibernate refreshes the kill/resume payload (pin + count survive).</summary>
    public void MarkHibernated(
        string resumeArgvJson, string? nativeSessionId, string? cwd,
        DateTime idleAtUtc, DateTime atUtc)
    {
        IsHibernated = true;
        HibernatedAtUtc = atUtc;
        IdleAtUtc = idleAtUtc;
        ResumeArgvJson = resumeArgvJson;
        NativeSessionId = nativeSessionId;
        Cwd = cwd;
    }

    public void MarkResumed(bool auto)
    {
        IsHibernated = false;
        if (auto)
        {
            AutoResumedCount++;
        }
    }

    public void SetPinned(bool pinned) => Pinned = pinned;

    /// <summary>mailbox-wake RF-002: stamp an auto-resume attempt.</summary>
    public void TouchAutoResume(DateTime atUtc) => LastAutoResumeUtc = atUtc;
}
