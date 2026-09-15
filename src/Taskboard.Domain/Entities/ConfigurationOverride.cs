using System;

namespace Taskboard.Domain.Entities;

/// <summary>
/// A runtime configuration override stored in the database. When present,
/// it wins over environment variables and appsettings for the same key.
/// </summary>
public sealed class ConfigurationOverride : Entity<Guid>
{
    /// <summary>Configuration key, e.g. <c>Logging:LogLevel:Default</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    public ConfigurationOverride(Guid id)
        : base(id)
    {
    }
}
