using System;

namespace Taskboard.Domain.Entities;

public sealed class UserPreference : Entity<Guid>
{
    public string Theme { get; set; } = "dark";

    public string? GitHubToken { get; set; }

    /// <summary>SPEC-20261020-session-history-parity RF-005: collapse empty CLI groups.</summary>
    public bool HideEmptySessionGroups { get; set; } = true;

    /// <summary>SPEC-20261020-agent-hibernation RF-010: per-user idle
    /// threshold in minutes (5–1440); null → server default 30.</summary>
    public int? AgentIdleThresholdMinutes { get; set; }

    /// <summary>SPEC-20261020-agent-hibernation RF-006: resume a hibernated
    /// session automatically when its card/row is focused.</summary>
    public bool AutoResumeOnFocus { get; set; } = true;

    public UserPreference(Guid id)
        : base(id)
    {
    }
}
