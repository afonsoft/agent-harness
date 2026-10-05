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
            "Taskboard:WebCliAgent:Enabled",
            "Taskboard:Agents:DefaultPrompt",
            "Taskboard:Chat:Tools:Enabled",
            "Taskboard:Chat:MaxToolIterations",
            "Taskboard:Chat:SearchBackend",
            "Taskboard:Chat:SearchUrl",
            "Taskboard:Chat:SearchApiKey",
            "Taskboard:Chat:DefaultChatModel",
            "Taskboard:Chat:DefaultCodeModel",
            "Taskboard:Chat:DefaultImageModel",
            "Taskboard:AiChat:DefaultMode",
            "Taskboard:Chat:Skills:Enabled",
            "Taskboard:Chat:AgentDelegation:Enabled",
            "Taskboard:Chat:Mcp:Enabled",
            "Taskboard:Chat:Mcp:Servers",
            "Taskboard:Chat:Mcp:CallTimeoutSeconds",
            "Taskboard:Chat:Mcp:IncludeGlobalAgents",
            "Taskboard:Chat:Capabilities:Disabled",
            "Taskboard:Chat:Runs:MaxConcurrent",
            "Taskboard:Chat:Runs:CheckpointMs",
            "Taskboard:Chat:Runs:RetentionDays",
            "Taskboard:Chat:Notify:Done:InApp",
            "Taskboard:Chat:Notify:Done:Browser",
            "Taskboard:Chat:Notify:Done:Push",
            "Taskboard:Push:Vapid:PublicKey",
            "Taskboard:Push:Vapid:PrivateKey",
            "Taskboard:Push:Vapid:Subject",
            "Taskboard:Cache:DefaultExpiration",
            "Taskboard:Cache:LocalCacheExpiration",
            "Taskboard:Cache:Redis:ConnectionString",
            "Taskboard:Cache:Redis:InstanceName",
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
    [InlineData("Logging:LogLevel:Default", "Verbose")]
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
    [InlineData("Taskboard:Cache:DefaultExpiration", "abc")]
    [InlineData("Taskboard:Cache:DefaultExpiration", "00:00:00")]
    [InlineData("Taskboard:Cache:LocalCacheExpiration", "not-a-timespan")]
    [InlineData("Taskboard:Cache:Redis:ConnectionString", "  ")]
    [InlineData("Taskboard:Cache:Redis:InstanceName", "has space")]
    [InlineData("Taskboard:Cache:Redis:InstanceName", "")]
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
    // SPEC-20261010-settings-configuration-tab RF-002: binding do servidor virou
    // read-only — via HARNESS_PORT/HARNESS_URL ou appsettings.json.
    [InlineData("Taskboard:Port")]
    [InlineData("Taskboard:BaseUrl")]
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
    public void Dado_RedisConnectionString_Quando_Listar_Entao_MascaradaERequiresRestart()
    {
        // SPEC-20261004-redis-hybrid-cache RF-002: connstring never leaves the API
        // in clear text; DI wiring only happens at boot.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                "Taskboard:Cache:Redis:ConnectionString", "localhost:6379,password=abc")])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:Cache:Redis:ConnectionString");

        entry.Masked.ShouldBeTrue();
        entry.Editable.ShouldBeTrue();
        entry.RequiresRestart.ShouldBeTrue();
        entry.EffectiveValue.ShouldBe("••••=abc");
    }

    [Fact]
    public async Task Dado_CacheExpirationValida_Quando_SetOverride_Entao_Persiste()
    {
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync("Taskboard:Cache:DefaultExpiration", "00:10:00");

        result.Error.ShouldBe(ConfigurationWriteError.None);
        context.ConfigurationOverrides
            .Single(o => o.Key == "Taskboard:Cache:DefaultExpiration")
            .Value.ShouldBe("00:10:00");
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
            Key = "AllowedHosts",
            Value = "example.com",
            UpdatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();

        var result = await service.DeleteOverrideAsync("AllowedHosts");

        result.Error.ShouldBe(ConfigurationWriteError.None);
        context.ConfigurationOverrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SemOverride_Quando_Delete_Entao_RetornaNotFound()
    {
        // Covers RF-005: deleting a missing override returns NotFound
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.DeleteOverrideAsync("AllowedHosts");

        result.Error.ShouldBe(ConfigurationWriteError.NotFound);
    }

    // SPEC-20260929-webcli-toggle-finops-active-sessions RF-001.
    [Fact]
    public void Dado_Catalogo_Quando_ListarWebCliAgent_Entao_DefaultTrueSemRestart()
    {
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:WebCliAgent:Enabled");

        entry.EffectiveValue.ShouldBe("true");
        entry.Source.ShouldBe("default");
        entry.Editable.ShouldBeTrue();
        entry.RequiresRestart.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_OverrideFalse_Quando_ListarComProviderSqlite_Entao_OverrideVence()
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"tb-svc-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using var context = new TaskboardDbContext(options);
        context.Database.EnsureCreated();
        var provider = new SqliteConfigurationProvider(dbPath);
        var configuration = new ConfigurationBuilder()
            .Add(new SqliteConfigurationSource(provider))
            .Build();
        var service = new RuntimeConfigurationService(
            configuration, new EfCoreRepository<ConfigurationOverride>(context));

        (await service.SetOverrideAsync("Taskboard:WebCliAgent:Enabled", "false"))
            .Error.ShouldBe(ConfigurationWriteError.None);
        provider.Reload();

        var entry = service.GetEntries().Single(e => e.Key == "Taskboard:WebCliAgent:Enabled");
        entry.EffectiveValue.ShouldBe("false");
        entry.Source.ShouldBe("db");
    }

    [Fact]
    public void Dado_EnvAlias_Quando_Definido_Entao_ValorEnvVence()
    {
        Environment.SetEnvironmentVariable("HARNESS_WEB_CLI_AGENT_ENABLED", "false");
        try
        {
            var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

            var entry = service.GetEntries().Single(e => e.Key == "Taskboard:WebCliAgent:Enabled");

            entry.EffectiveValue.ShouldBe("false");
            entry.Source.ShouldBe("env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARNESS_WEB_CLI_AGENT_ENABLED", null);
        }
    }

    // B-22: o gate dos endpoints deve resolver pela mesma fonte do catálogo —
    // o alias env vence o valor configurado em Taskboard:WebCliAgent:Enabled.
    [Fact]
    public void Dado_EnvAliasFalseEConfigTrue_Quando_GetEffectiveBool_Entao_False()
    {
        Environment.SetEnvironmentVariable("HARNESS_WEB_CLI_AGENT_ENABLED", "false");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:WebCliAgent:Enabled"] = "true",
                })
                .Build();
            var (service, _, _) = CreateSut(configuration);

            service.GetEffectiveBool("Taskboard:WebCliAgent:Enabled").ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARNESS_WEB_CLI_AGENT_ENABLED", null);
        }
    }

    [Fact]
    public void Dado_SemOverrideNemAlias_Quando_GetEffectiveBool_Entao_DefaultDoCatalogo()
    {
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        service.GetEffectiveBool("Taskboard:WebCliAgent:Enabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ValorNaoBooleano_Quando_SetOverrideWebCliAgent_Entao_RetornaValidation()
    {
        var (service, context, _) = CreateSut(new ConfigurationBuilder().Build());

        var result = await service.SetOverrideAsync("Taskboard:WebCliAgent:Enabled", "yes");

        result.Error.ShouldBe(ConfigurationWriteError.Validation);
        context.ConfigurationOverrides.ShouldBeEmpty();
    }

    // SPEC-20261010-settings-configuration-tab RF-001.
    [Fact]
    public void Dado_Catalogo_Quando_Listar_Entao_GroupEManagedInProjetados()
    {
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var entries = service.GetEntries();

        var port = entries.Single(e => e.Key == "Taskboard:Port");
        port.Group.ShouldBe("Server");
        port.ManagedIn.ShouldBeNull();
        port.Editable.ShouldBeFalse();
        port.ReadOnlyReason.ShouldNotBeNullOrEmpty();

        var cache = entries.Single(e => e.Key == "Taskboard:Cache:Redis:ConnectionString");
        cache.Group.ShouldBe("Connections");
        cache.ManagedIn.ShouldBeNull();
    }

    [Theory]
    [InlineData("Taskboard:Agents:DefaultPrompt", "/agents?tab=prompt")]
    [InlineData("Taskboard:Terminal:Enabled", "/settings?tab=general")]
    [InlineData("Taskboard:WebCliAgent:Enabled", "/settings?tab=general")]
    [InlineData("Taskboard:Skills:Repository", "/settings?tab=mcp-skills")]
    [InlineData("Taskboard:Rag:ServerName", "/settings?tab=integrations")]
    [InlineData("Taskboard:Rag:Url", "/settings?tab=integrations")]
    [InlineData("Taskboard:Rag:ApiKey", "/settings?tab=integrations")]
    [InlineData("Taskboard:Chat:SearchBackend", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:SearchUrl", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:SearchApiKey", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:DefaultChatModel", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:DefaultCodeModel", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:DefaultImageModel", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:Capabilities:Disabled", "/settings?tab=chat")]
    [InlineData("Taskboard:Chat:Mcp:Enabled", "/settings?tab=mcp-skills")]
    [InlineData("Taskboard:Chat:Mcp:Servers", "/settings?tab=mcp-skills")]
    [InlineData("Taskboard:Chat:Mcp:CallTimeoutSeconds", "/settings?tab=mcp-skills")]
    [InlineData("Taskboard:Chat:Mcp:IncludeGlobalAgents", "/settings?tab=mcp-skills")]
    public void Dado_ChaveComTelaDedicada_Quando_Listar_Entao_ManagedInRota(string key, string expectedRoute)
    {
        // SPEC-20261010-settings-configuration-tab RF-001 dedupe map: chaves com
        // UI dedicada carregam a rota — a aba Configuration as esconde da tabela.
        var (service, _, _) = CreateSut(new ConfigurationBuilder().Build());

        var entry = service.GetEntries().Single(e => e.Key == key);

        entry.ManagedIn.ShouldBe(expectedRoute);
    }

    [Fact]
    public void Dado_SqliteSemRedis_Quando_GetConnectionInfo_Entao_SqliteEMemory()
    {
        // SPEC-20261010 RF-003: defaults de hoje — SQLite + cache L1-only.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                "ConnectionStrings:Taskboard", "Data Source=/var/harness.sqlite")])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var info = service.GetConnectionInfo();

        info.DbProvider.ShouldBe("sqlite");
        info.CacheMode.ShouldBe("memory");
        info.DbConnectionName.ShouldBe("Taskboard");
        // connstring chega mascarada, nunca em claro
        info.DbConnectionString.ShouldBe("••••lite");
    }

    [Fact]
    public void Dado_RedisConfigurado_Quando_GetConnectionInfo_Entao_Redis()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Taskboard:Cache:Redis:ConnectionString", "localhost:6379"),
                new KeyValuePair<string, string?>("Taskboard:Cache:Redis:InstanceName", "harness:"),
            ])
            .Build();
        var (service, _, _) = CreateSut(configuration);

        var info = service.GetConnectionInfo();

        info.CacheMode.ShouldBe("redis");
        info.CacheInstanceName.ShouldBe("harness:");
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
