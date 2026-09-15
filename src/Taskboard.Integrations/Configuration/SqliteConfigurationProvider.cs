using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.Integrations.Configuration;

/// <summary>
/// Loads <c>ConfigurationOverrides</c> rows from the taskboard SQLite database into
/// configuration. Registered last so database values win over environment variables
/// and appsettings. Reads are best-effort: a missing database or table yields no
/// overrides and never fails startup.
/// </summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider, IOverrideConfigurationProvider
{
    private readonly string _databasePath;

    public SqliteConfigurationProvider(string databasePath)
    {
        _databasePath = databasePath;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_databasePath))
            {
                using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT \"Key\", \"Value\" FROM \"ConfigurationOverrides\"";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    data[reader.GetString(0)] = reader.GetString(1);
                }
            }
        }
        catch
        {
            // Best effort: an unreadable or unmigrated database means no overrides.
        }

        Data = data;
    }

    /// <summary>Re-reads the database and notifies consumers via the change token.</summary>
    public void Reload()
    {
        Load();
        OnReload();
    }

    public bool TryGetOverride(string key, out string? value) => Data.TryGetValue(key, out value);
}

/// <summary>Configuration source wrapping <see cref="SqliteConfigurationProvider"/>.</summary>
public sealed class SqliteConfigurationSource : IConfigurationSource
{
    private readonly SqliteConfigurationProvider _provider;

    public SqliteConfigurationSource(SqliteConfigurationProvider provider)
    {
        _provider = provider;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;
}
