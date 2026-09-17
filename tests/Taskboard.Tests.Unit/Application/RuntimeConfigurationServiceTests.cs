using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Configuration;
using Taskboard.EntityFrameworkCore.Data;
using ConfigurationOverride = Taskboard.Domain.Entities.ConfigurationOverride;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Configuration;
using Xunit;

namespace Taskboard.Tests.Unit.Application;

public class RuntimeConfigurationServiceTests
{
    [Fact]
    public void Dado_ConfiguracaoPadrao_Quando_Listar_Entao_RetornaCatalogoComSourceDefault()
    {
        // Covers RF-003: catalog exposes all known keys with defaults
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var entries = service.GetEntries();

        entries.Select(e => e.Key).ShouldBe([
            "Taskboard:Port",
            "Taskboard:BaseUrl",
            "AllowedHosts",
            "Logging:LogLevel:Default",
            "Logging:LogLevel:Microsoft.AspNetCore",
            "Taskboard:DataDir",
            "Taskboard:Database:ConnectionStringName",
            "ConnectionStrings:Taskboard",
            "Admin:Username",
            "Taskboard:Skills:Repository",
            "Taskboard:ApiKey",
            "Taskboard:Rag:ServerName",
            "Taskboard:Rag:Url",
            "Taskboard:Rag:ApiKey",
            "Taskboard:Terminal:Enabled",
        ]);
        entries.All(e => e.Source == "default" || e.Source == "appsettings" || e.Source == "env").ShouldBeTrue();
    }

    [Fact]
    public void Dado_ValorEmAppSettings_Quando_Listar_Entao_SourceAppSettings()
    {
        // Covers RF-003: a configured key reports source=appsettings
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Taskboard:Port", "9090")])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:Port");

        entry.EffectiveValue.ShouldBe("9090");
        entry.Source.ShouldBe("appsettings");
    }

    [Fact]
    public void Dado_OverrideNoBanco_Quando_Listar_Entao_SourceDb()
    {
        // Covers RF-003: a DB override reports source=db
        var (service, context, dbPath) = CreateSut(new ConfigurationBuilder().Build());
        context.ConfigurationOverrides.Add(new ConfigurationOverride(Guid.NewGuid())
        {
            Key = "Logging:LogLevel:Default",
            Value = "Debug",
            UpdatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();

        var provider = new SqliteConfigurationProvider(dbPath);
        var root = new ConfigurationBuilder()
            .Add(new SqliteConfigurationSource(provider))
            .Build();
        var serviceWithDb = new RuntimeConfigurationService(
            root,
            new EfCoreRepository<ConfigurationOverride>(context));

        var entry = serviceWithDb.GetEntries().Single(e => e.Key == "Logging:LogLevel:Default");

        entry.EffectiveValue.ShouldBe("Debug");
        entry.Source.ShouldBe("db");
    }

    [Fact]
    public void Dado_ChaveSecreta_Quando_Listar_Entao_ValorMascarado()
    {
        // Covers RF-003: connection strings never leave the API in clear text
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                "ConnectionStrings:Taskboard", "Data Source=/var/taskboard.sqlite")])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var entry = service.GetEntries().Single(e => e.Key == "ConnectionStrings:Taskboard");

        entry.Masked.ShouldBeTrue();
        entry.EffectiveValue.ShouldBe("••••lite");
        entry.Editable.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ValorValido_Quando_SetOverride_Entao_Persiste()
    {
        // Covers RF-004: valid value persists as a DB override
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync("Logging:LogLevel:Default", "Warning");

        result.Error.ShouldBe(ConfigurationWriteError.None);
        var row = context.ConfigurationOverrides.Single(o => o.Key == "Logging:LogLevel:Default");
        row.Value.ShouldBe("Warning");
    }

    [Theory]
    [InlineData("Taskboard:Port", "abc")]
    [InlineData("Taskboard:Port", "99999")]
    [InlineData("Logging:LogLevel:Default", "Verbose")]
    [InlineData("Taskboard:BaseUrl", "not-a-url")]
    [InlineData("AllowedHosts", "")]
    [InlineData("Taskboard:Skills:Repository", "not a repo")]
    [InlineData("Taskboard:Skills:Repository", "ftp://example.com/repo")]
    [InlineData("Taskboard:Rag:Url", "not-a-url")]
    [InlineData("Taskboard:Rag:Url", "ftp://rag.example.com/mcp")]
    [InlineData("Taskboard:Rag:ServerName", "UPPER")]
    [InlineData("Taskboard:Rag:ServerName", "-leading-dash")]
    [InlineData("Taskboard:Rag:ServerName", "has space")]
    [InlineData("Taskboard:Rag:ServerName", "")]
    [InlineData("Taskboard:Rag:ApiKey", "short")]
    public async Task Dado_ValorInvalido_Quando_SetOverride_Entao_RetornaValidation(string key, string value)
    {
        // Covers RF-004: per-key validation rejects bad values without persisting
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync(key, value);

        result.Error.ShouldBe(ConfigurationWriteError.Validation);
        context.ConfigurationOverrides.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Taskboard:DataDir")]
    [InlineData("ConnectionStrings:Taskboard")]
    [InlineData("Admin:Username")]
    public async Task Dado_ChaveReadOnly_Quando_SetOverride_Entao_RetornaReadOnly(string key)
    {
        // Covers RF-004 / invariants: chicken-egg and admin keys never persist overrides
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync(key, "/x");

        result.Error.ShouldBe(ConfigurationWriteError.ReadOnly);
        context.ConfigurationOverrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RagValido_Quando_SetOverride_Entao_PersisteEmSqlite()
    {
        // Covers SPEC-20260917-rag-mcp-provisioning RF-001: RAG keys persist via ConfigurationOverride
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        (await service.SetOverrideAsync("Taskboard:Rag:Url", "https://rag.afonsoft.dev/mcp"))
            .Error.ShouldBe(ConfigurationWriteError.None);
        (await service.SetOverrideAsync("Taskboard:Rag:ApiKey", "aft_0123456789abcdef"))
            .Error.ShouldBe(ConfigurationWriteError.None);

        context.ConfigurationOverrides.Select(o => o.Key).ShouldBe(
            ["Taskboard:Rag:Url", "Taskboard:Rag:ApiKey"], ignoreOrder: true);
    }

    [Fact]
    public void Dado_RagApiKey_Quando_Listar_Entao_Mascarada()
    {
        // Covers SPEC-20260917-rag-mcp-provisioning: API key never leaves the API in clear text
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                "Taskboard:Rag:ApiKey", "aft_0123456789abcdef")])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:Rag:ApiKey");

        entry.Masked.ShouldBeTrue();
        entry.EffectiveValue.ShouldBe("••••cdef");
    }

    [Fact]
    public void Dado_RagServerName_Quando_Listar_Entao_DefaultKnowledge()
    {
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:Rag:ServerName");

        entry.EffectiveValue.ShouldBe("knowledge");
    }

    [Fact]
    public async Task Dado_ChaveDesconhecida_Quando_SetOverride_Entao_RetornaValidation()
    {
        // Covers edge case: catalog is a closed set
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync("Foo:Bar", "x");

        result.Error.ShouldBe(ConfigurationWriteError.Validation);
    }

    [Fact]
    public async Task Dado_OverrideExistente_Quando_Delete_Entao_Remove()
    {
        // Covers RF-005: delete removes the row
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());
        context.ConfigurationOverrides.Add(new ConfigurationOverride(Guid.NewGuid())
        {
            Key = "Taskboard:Port",
            Value = "5000",
            UpdatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();

        var result = await service.DeleteOverrideAsync("Taskboard:Port");

        result.Error.ShouldBe(ConfigurationWriteError.None);
        context.ConfigurationOverrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SemOverride_Quando_Delete_Entao_RetornaNotFound()
    {
        // Covers RF-005: deleting a missing override returns NotFound
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.DeleteOverrideAsync("Taskboard:Port");

        result.Error.ShouldBe(ConfigurationWriteError.NotFound);
    }

    private static (RuntimeConfigurationService Service, TaskboardDbContext Context, string DbPath) CreateSut(
        IConfiguration configuration)
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"tb-svc-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var context = new TaskboardDbContext(options);
        context.Database.EnsureCreated();
        var repository = new EfCoreRepository<ConfigurationOverride>(context);
        return (new RuntimeConfigurationService(configuration, repository), context, dbPath);
    }
}
