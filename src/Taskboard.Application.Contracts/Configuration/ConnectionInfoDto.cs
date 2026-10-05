namespace Taskboard.Application.Contracts.Configuration;

/// <summary>
/// Read-only summary of the database provider and cache mode shown at the top
/// of the Settings Configuration tab
/// (SPEC-20261010-settings-configuration-tab RF-003).
/// </summary>
/// <param name="DbProvider"><c>sqlite</c> or <c>postgresql</c>.</param>
/// <param name="DbConnectionName">
/// Effective <c>Taskboard:Database:ConnectionStringName</c> — the key of the
/// active connection string.
/// </param>
/// <param name="DbConnectionString">
/// The active connection string, masked to the last 4 characters.
/// </param>
/// <param name="CacheMode"><c>memory</c> (L1 only) or <c>redis</c> (L1+L2).</param>
/// <param name="CacheInstanceName">
/// Effective <c>Taskboard:Cache:Redis:InstanceName</c> — only meaningful when
/// <paramref name="CacheMode"/> is <c>redis</c>.
/// </param>
public sealed record ConnectionInfoDto(
    string DbProvider,
    string? DbConnectionName,
    string? DbConnectionString,
    string CacheMode,
    string? CacheInstanceName);
