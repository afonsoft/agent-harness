using System;
using System.Buffers.Text;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Configuration;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Configuration;
using Taskboard.Server.Services;
using Xunit;
using ConfigurationOverride = Taskboard.Domain.Entities.ConfigurationOverride;

namespace Taskboard.Tests.Unit.Application;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: o VapidKeyService gera o par
/// ECDSA P-256 na primeira chamada, persiste via override + recarrega o
/// provider (mesmo processo já enxerga) e reutiliza o mesmo par em qualquer
/// serviço lendo o mesmo banco.
/// </summary>
public class VapidKeyServiceTests
{
    /// <summary>
    /// Wiring igual ao Program.cs: SqliteConfigurationProvider (dbPath) como
    /// source do IConfiguration + repositório EF sobre o MESMO banco.
    /// </summary>
    private static (
        RuntimeConfigurationService Configuration,
        SqliteConfigurationProvider Provider,
        TaskboardDbContext Context,
        string DbPath) CreateSut(string dbPath, IConfiguration? extra = null)
    {
        var provider = new SqliteConfigurationProvider(dbPath);
        var builder = new ConfigurationBuilder();
        if (extra is not null)
        {
            builder.AddConfiguration(extra);
        }

        builder.Add(new SqliteConfigurationSource(provider));
        var configuration = builder.Build();

        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var context = new TaskboardDbContext(options);
        context.Database.EnsureCreated();
        var repository = new EfCoreRepository<ConfigurationOverride>(context);
        return (new RuntimeConfigurationService(configuration, repository), provider, context, dbPath);
    }

    private static string NewDbPath() =>
        Path.Join(Path.GetTempPath(), $"tb-vapid-{Guid.NewGuid()}.sqlite");

    private static void Cleanup(TaskboardDbContext context, string dbPath)
    {
        context.Dispose();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Dado_CatalogoVazio_Quando_GetOrCreate_Entao_GeraPersisteEReusaMesmoPar()
    {
        var dbPath = NewDbPath();
        var (configuration, provider, context, _) = CreateSut(dbPath);
        var sut = new VapidKeyService(
            configuration, provider, NullLogger<VapidKeyService>.Instance);
        try
        {
            var keys = await sut.GetOrCreateAsync(CancellationToken.None);

            var publicBytes = Base64Url.DecodeFromChars(keys.PublicKey);
            publicBytes.Length.ShouldBe(65);
            publicBytes[0].ShouldBe((byte)0x04);

            // O par deve carregar num ECDsa — prova que é uma chave real, não lixo.
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(SubjectPublicKeyInfo(publicBytes), out _);
            keys.Subject.ShouldBe("mailto:admin@localhost");

            // Persistiu como override E o provider já recarregou — a MESMA
            // chamada num serviço novo (mesmo banco) devolve o MESMO par.
            var (configuration2, provider2, context2, _) = CreateSut(dbPath);
            var sut2 = new VapidKeyService(
                configuration2, provider2, NullLogger<VapidKeyService>.Instance);
            try
            {
                var again = await sut2.GetOrCreateAsync(CancellationToken.None);

                again.PublicKey.ShouldBe(keys.PublicKey);
                again.PrivateKey.ShouldBe(keys.PrivateKey);
            }
            finally
            {
                context2.Dispose();
            }
        }
        finally
        {
            Cleanup(context, dbPath);
        }
    }

    [Fact]
    public async Task Dado_ParJaConfigurado_Quando_GetOrCreate_Entao_RetornaSemGerar()
    {
        var extra = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Taskboard:Push:Vapid:PublicKey", "configured-public"),
                new KeyValuePair<string, string?>("Taskboard:Push:Vapid:PrivateKey", "configured-private"),
            ])
            .Build();
        var dbPath = NewDbPath();
        var (configuration, provider, context, _) = CreateSut(dbPath, extra);
        var sut = new VapidKeyService(
            configuration, provider, NullLogger<VapidKeyService>.Instance);
        try
        {
            var keys = await sut.GetOrCreateAsync(CancellationToken.None);

            keys.PublicKey.ShouldBe("configured-public");
            keys.PrivateKey.ShouldBe("configured-private");
        }
        finally
        {
            Cleanup(context, dbPath);
        }
    }

    private static byte[] SubjectPublicKeyInfo(byte[] uncompressedPoint)
    {
        // SPKI wrapping da P-256: cabeçalho DER fixo para EC P-256 + ponto.
        byte[] prefix =
        [
            0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02,
            0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03,
            0x42, 0x00,
        ];
        var spki = new byte[prefix.Length + uncompressedPoint.Length];
        prefix.CopyTo(spki, 0);
        uncompressedPoint.CopyTo(spki, prefix.Length);
        return spki;
    }
}
