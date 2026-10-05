using System;
using System.Collections.Generic;
using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.Application.Configuration;

/// <summary>
/// SPEC-20261010-settings-configuration-tab RF-003: classifies the database
/// provider and cache mode from the effective configuration entries. Pure —
/// pass the <b>unmasked</b> entry list; the connection string is masked in the
/// result.
/// </summary>
public static class ConnectionInfoResolver
{
    public const string DbProviderSqlite = "sqlite";
    public const string DbProviderPostgres = "postgresql";
    public const string CacheModeMemory = "memory";
    public const string CacheModeRedis = "redis";

    public static ConnectionInfoDto Resolve(IReadOnlyList<ConfigurationEntryDto> entries)
    {
        var connectionName = ValueOf(entries, "Taskboard:Database:ConnectionStringName");
        var connectionString = ValueOf(
            entries,
            $"ConnectionStrings:{connectionName ?? "Taskboard"}");
        var redis = ValueOf(entries, "Taskboard:Cache:Redis:ConnectionString");
        var redisOn = !string.IsNullOrWhiteSpace(redis);

        return new ConnectionInfoDto(
            DetectDbProvider(connectionString),
            connectionName,
            Mask(connectionString),
            redisOn ? CacheModeRedis : CacheModeMemory,
            ValueOf(entries, "Taskboard:Cache:Redis:InstanceName"));
    }

    private static string DetectDbProvider(string? connectionString)
    {
        // SQLite is the only provider the backend wires today; Npgsql markers
        // are recognized up front so the badge is correct when Postgres lands.
        if (!string.IsNullOrEmpty(connectionString)
            && (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)))
        {
            return DbProviderPostgres;
        }

        return DbProviderSqlite;
    }

    private static string? ValueOf(IReadOnlyList<ConfigurationEntryDto> entries, string key)
    {
        foreach (var entry in entries)
        {
            if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.EffectiveValue;
            }
        }

        return null;
    }

    // Same masking as RuntimeConfigurationService — keep in sync.
    private static string? Mask(string? value)
    {
        if (value is null)
        {
            return null;
        }
        var tail = value.Length > 4 ? value[^4..] : string.Empty;
        return $"••••{tail}";
    }
}
