using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Configuration;

/// <summary>
/// Exposes the known configuration catalog: effective value, source
/// (<c>db|env|appsettings|default</c>) and per-key validation for database overrides.
/// </summary>
public sealed class RuntimeConfigurationService
{
    private static readonly string[] LogLevels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    private static readonly CatalogEntry[] Catalog =
    [
        new("Taskboard:Port", "47823", Editable: true, RequiresRestart: true, ReadOnlyReason: null,
            EnvAlias: "TASKBOARD_PORT",
            Validate: v => int.TryParse(v, out var p) && p is >= 1 and <= 65535
                ? null
                : "Port must be an integer between 1 and 65535."),
        new("Taskboard:BaseUrl", "http://127.0.0.1:47823", Editable: true, RequiresRestart: true, ReadOnlyReason: null,
            EnvAlias: "TASKBOARD_URL",
            Validate: v => Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
                ? null
                : "BaseUrl must be an absolute http(s) URL."),
        new("AllowedHosts", "*", Editable: true, RequiresRestart: true, ReadOnlyReason: null,
            EnvAlias: null,
            Validate: v => string.IsNullOrWhiteSpace(v) ? "AllowedHosts cannot be empty." : null),
        new("Logging:LogLevel:Default", "Information", Editable: true, RequiresRestart: false, ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateLogLevel),
        new("Logging:LogLevel:Microsoft.AspNetCore", "Warning", Editable: true, RequiresRestart: false, ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateLogLevel),
        new("Taskboard:DataDir", ".data", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: "TASKBOARD_DATA_DIR", Validate: null),
        new("Taskboard:Database:ConnectionStringName", "Taskboard", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: null, Validate: null),
        new("ConnectionStrings:Taskboard", null, Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: null, Validate: null),
        new("Admin:Username", "admin", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Managed by the admin account (admin.json).",
            EnvAlias: "TASKBOARD_ADMIN_USERNAME", Validate: null),
    ];

    private readonly IConfiguration _configuration;
    private readonly IRepository<ConfigurationOverride> _overrides;

    public RuntimeConfigurationService(IConfiguration configuration, IRepository<ConfigurationOverride> overrides)
    {
        _configuration = configuration;
        _overrides = overrides;
    }

    /// <summary>Returns every catalog key with effective value, source and editability.</summary>
    public IReadOnlyList<ConfigurationEntryDto> GetEntries()
    {
        return Catalog.Select(entry =>
        {
            var effective = ResolveValue(entry);
            var masked = IsSecret(entry.Key);
            return new ConfigurationEntryDto(
                entry.Key,
                masked ? Mask(effective) : effective,
                ResolveSource(entry),
                entry.Editable,
                entry.RequiresRestart,
                masked,
                entry.ReadOnlyReason);
        }).ToList();
    }

    /// <summary>Validates and persists a database override for <paramref name="key"/>.</summary>
    public async Task<ConfigurationWriteResult> SetOverrideAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, "Unknown configuration key.");
        }

        if (!entry.Editable)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.ReadOnly, "This key cannot be overridden.");
        }

        var validationError = entry.Validate?.Invoke(value ?? string.Empty);
        if (validationError is not null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, validationError);
        }

        var existing = await _overrides.Query
            .FirstOrDefaultAsync(o => o.Key == entry.Key, cancellationToken);
        if (existing is null)
        {
            await _overrides.AddAsync(
                new ConfigurationOverride(Guid.NewGuid())
                {
                    Key = entry.Key,
                    Value = value ?? string.Empty,
                    UpdatedAt = DateTime.UtcNow,
                },
                cancellationToken);
        }
        else
        {
            existing.Value = value ?? string.Empty;
            existing.UpdatedAt = DateTime.UtcNow;
            await _overrides.UpdateAsync(existing, cancellationToken);
        }

        await _overrides.SaveChangesAsync(cancellationToken);
        return ConfigurationWriteResult.Ok;
    }

    /// <summary>Removes the database override for <paramref name="key"/>.</summary>
    public async Task<ConfigurationWriteResult> DeleteOverrideAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, "Unknown configuration key.");
        }

        if (!entry.Editable)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.ReadOnly, "This key cannot be overridden.");
        }

        var existing = await _overrides.Query
            .FirstOrDefaultAsync(o => o.Key == entry.Key, cancellationToken);
        if (existing is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.NotFound, "No override exists for this key.");
        }

        await _overrides.DeleteAsync(existing, cancellationToken);
        await _overrides.SaveChangesAsync(cancellationToken);
        return ConfigurationWriteResult.Ok;
    }

    private static CatalogEntry? FindEntry(string key) =>
        Catalog.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private bool HasDatabaseOverride(string key) =>
        _configuration is IConfigurationRoot root
        && root.Providers.OfType<IOverrideConfigurationProvider>()
            .Any(p => p.TryGetOverride(key, out _));

    private string? ResolveValue(CatalogEntry entry)
    {
        // A DB override is already visible through configuration (the provider is
        // registered last). Otherwise a dedicated env alias (e.g. TASKBOARD_PORT)
        // beats Taskboard__* env vars and appsettings.
        if (!HasDatabaseOverride(entry.Key)
            && entry.EnvAlias is not null
            && Environment.GetEnvironmentVariable(entry.EnvAlias) is { Length: > 0 } envValue)
        {
            return envValue.Trim();
        }

        return _configuration[entry.Key] ?? entry.DefaultValue;
    }

    private string ResolveSource(CatalogEntry entry)
    {
        // Mirror the effective precedence: db > env alias > Taskboard__* env > appsettings > default.
        if (HasDatabaseOverride(entry.Key))
        {
            return "db";
        }

        if (entry.EnvAlias is not null
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(entry.EnvAlias)))
        {
            return "env";
        }

        if (_configuration is IConfigurationRoot configRoot)
        {
            foreach (var provider in configRoot.Providers.Reverse())
            {
                if (provider.TryGet(entry.Key, out _))
                {
                    return provider.GetType().Name.Contains("EnvironmentVariables", StringComparison.Ordinal)
                        ? "env"
                        : "appsettings";
                }
            }
        }

        return _configuration[entry.Key] is not null ? "appsettings" : "default";
    }

    private static bool IsSecret(string key) =>
        key.Contains("Password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("Token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase);

    private static string? Mask(string? value) =>
        value is null ? null : $"••••{(value.Length > 4 ? value[^4..] : string.Empty)}";

    private static string? ValidateLogLevel(string value) =>
        LogLevels.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"Log level must be one of: {string.Join(", ", LogLevels)}.";

    private sealed record CatalogEntry(
        string Key,
        string? DefaultValue,
        bool Editable,
        bool RequiresRestart,
        string? ReadOnlyReason,
        string? EnvAlias,
        Func<string, string?>? Validate);
}

public enum ConfigurationWriteError
{
    None,
    Validation,
    ReadOnly,
    NotFound
}

public sealed record ConfigurationWriteResult(ConfigurationWriteError Error, string? Message)
{
    public static readonly ConfigurationWriteResult Ok = new(ConfigurationWriteError.None, null);

    public static ConfigurationWriteResult Fail(ConfigurationWriteError error, string message) => new(error, message);
}
