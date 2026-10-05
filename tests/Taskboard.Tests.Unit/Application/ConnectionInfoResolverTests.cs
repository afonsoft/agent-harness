using System.Collections.Generic;
using Shouldly;
using Taskboard.Application.Configuration;
using Taskboard.Application.Contracts.Configuration;
using Xunit;

namespace Taskboard.Tests.Unit.Application;

/// <summary>
/// SPEC-20261010-settings-configuration-tab RF-003: classificação do provider
/// do banco e do modo de cache a partir das entradas efetivas.
/// </summary>
public class ConnectionInfoResolverTests
{
    private static ConfigurationEntryDto Entry(string key, string? value) =>
        new(key, value, "default", Editable: false, RequiresRestart: true, Masked: false,
            ReadOnlyReason: null, Group: "Connections", ManagedIn: null);

    [Fact]
    public void Dado_ConnStringSqlite_Quando_Resolver_Entao_ProviderSqlite()
    {
        var info = ConnectionInfoResolver.Resolve(
        [
            Entry("Taskboard:Database:ConnectionStringName", "Taskboard"),
            Entry("ConnectionStrings:Taskboard", "Data Source=/var/harness.sqlite"),
        ]);

        info.DbProvider.ShouldBe("sqlite");
        info.DbConnectionName.ShouldBe("Taskboard");
        info.DbConnectionString.ShouldBe("••••lite");
    }

    [Fact]
    public void Dado_ConnStringPostgres_Quando_Resolver_Entao_ProviderPostgresql()
    {
        var info = ConnectionInfoResolver.Resolve(
        [
            Entry("Taskboard:Database:ConnectionStringName", "Taskboard"),
            Entry("ConnectionStrings:Taskboard", "Host=db.internal;Database=harness;Username=app"),
        ]);

        info.DbProvider.ShouldBe("postgresql");
    }

    [Fact]
    public void Dado_ConnStringDeOutroNome_Quando_Resolver_Entao_SegueConnectionStringName()
    {
        var info = ConnectionInfoResolver.Resolve(
        [
            Entry("Taskboard:Database:ConnectionStringName", "Reporting"),
            Entry("ConnectionStrings:Taskboard", "Data Source=/var/harness.sqlite"),
            Entry("ConnectionStrings:Reporting", "Host=reports.internal;Database=r"),
        ]);

        info.DbProvider.ShouldBe("postgresql");
        info.DbConnectionName.ShouldBe("Reporting");
    }

    [Fact]
    public void Dado_SemEntradas_Quando_Resolver_Entao_DefaultsSqliteMemory()
    {
        var info = ConnectionInfoResolver.Resolve([]);

        info.DbProvider.ShouldBe("sqlite");
        info.CacheMode.ShouldBe("memory");
        info.DbConnectionName.ShouldBeNull();
        info.DbConnectionString.ShouldBeNull();
    }

    [Fact]
    public void Dado_RedisVazio_Quando_Resolver_Entao_Memory()
    {
        var info = ConnectionInfoResolver.Resolve(
            [Entry("Taskboard:Cache:Redis:ConnectionString", "   ")]);

        info.CacheMode.ShouldBe("memory");
    }

    [Fact]
    public void Dado_RedisSet_Quando_Resolver_Entao_RedisComInstanceName()
    {
        var info = ConnectionInfoResolver.Resolve(
        [
            Entry("Taskboard:Cache:Redis:ConnectionString", "localhost:6379"),
            Entry("Taskboard:Cache:Redis:InstanceName", "harness:"),
        ]);

        info.CacheMode.ShouldBe("redis");
        info.CacheInstanceName.ShouldBe("harness:");
    }
}
