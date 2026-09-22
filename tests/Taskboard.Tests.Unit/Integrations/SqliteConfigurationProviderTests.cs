using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Integrations.Configuration;
using Xunit;
using TaskboardEnvironment = Taskboard.Application.Contracts.Configuration.TaskboardEnvironment;

namespace Taskboard.Tests.Unit.Integrations;

public class SqliteConfigurationProviderTests
{
    [Fact]
    public void Dado_DbInexistente_Quando_Carregar_Entao_ProviderVazio()
    {
        // Covers RF-002: missing database yields no overrides and never throws
        var provider = new SqliteConfigurationProvider(Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.sqlite"));

        provider.Load();

        provider.TryGetOverride("Taskboard:Port", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_DbSemTabela_Quando_Carregar_Entao_ProviderVazio()
    {
        // Covers RF-002: unmigrated database yields no overrides and never throws
        var dbPath = CreateDatabaseWithoutTable();
        var provider = new SqliteConfigurationProvider(dbPath);

        provider.Load();

        provider.TryGetOverride("Taskboard:Port", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_OverrideNoDb_Quando_Carregar_Entao_ConfigurationRetornaValorDoBanco()
    {
        // Covers RF-002 / AC-01: DB override wins over env vars and appsettings
        var dbPath = CreateDatabase(("Taskboard:Port", "5000"));
        var provider = new SqliteConfigurationProvider(dbPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Taskboard:Port", "9090")])
            .Add(new SqliteConfigurationSource(provider))
            .Build();

        configuration["Taskboard:Port"].ShouldBe("5000");
    }

    [Fact]
    public void Dado_NovaLinhaAposLoad_Quando_Reload_Entao_NovoValorDisponivel()
    {
        // Covers RF-002: Reload picks up rows written after the first load
        var dbPath = CreateDatabase(("Taskboard:Port", "5000"));
        var provider = new SqliteConfigurationProvider(dbPath);
        provider.Load();

        InsertRow(dbPath, "Logging:LogLevel:Default", "Debug");
        provider.Reload();

        provider.TryGetOverride("Logging:LogLevel:Default", out var value).ShouldBeTrue();
        value.ShouldBe("Debug");
    }

    [Fact]
    public void Dado_OverrideDePortaNoDbEEnvSetado_Quando_GetPort_Entao_BancoVence()
    {
        // Covers AC-01: HARNESS_PORT env + DB row -> GetPort resolves the DB value
        var dbPath = CreateDatabase(("Taskboard:Port", "5000"));
        var provider = new SqliteConfigurationProvider(dbPath);
        var configuration = new ConfigurationBuilder()
            .Add(new SqliteConfigurationSource(provider))
            .Build();
        var hostEnvironment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        hostEnvironment.ContentRootPath.Returns("/app");

        var original = Environment.GetEnvironmentVariable("HARNESS_PORT");
        Environment.SetEnvironmentVariable("HARNESS_PORT", "6000");
        try
        {
            var environment = new TaskboardEnvironment(configuration, hostEnvironment);

            environment.GetPort().ShouldBe(5000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARNESS_PORT", original);
        }
    }

    private static string CreateDatabase(params (string Key, string Value)[] rows)
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"tb-config-{Guid.NewGuid()}.sqlite");
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "ConfigurationOverrides" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Key" TEXT NOT NULL,
                    "Value" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        foreach (var (key, value) in rows)
        {
            InsertRow(connection, key, value);
        }

        return dbPath;
    }

    private static string CreateDatabaseWithoutTable()
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"tb-config-{Guid.NewGuid()}.sqlite");
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE \"Other\" (\"Id\" INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void InsertRow(string dbPath, string key, string value)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        InsertRow(connection, key, value);
    }

    private static void InsertRow(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "ConfigurationOverrides" ("Id", "Key", "Value", "UpdatedAt")
            VALUES ($id, $key, $value, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
