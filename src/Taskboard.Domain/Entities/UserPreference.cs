using System;

namespace Taskboard.Domain.Entities;

public sealed class UserPreference : Entity<Guid>
{
    public string Theme { get; set; } = "dark";

    public string? GitHubToken { get; set; }

    /// <summary>SPEC-20261020-session-history-parity RF-005: collapse empty CLI groups.</summary>
    public bool HideEmptySessionGroups { get; set; } = true;

    public UserPreference(Guid id)
        : base(id)
    {
    }
}
