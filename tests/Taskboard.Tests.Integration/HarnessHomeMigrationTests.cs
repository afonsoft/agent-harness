using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Taskboard.Server;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260928-taskboard-env-fallback-removal RF-002: a legacy
/// <c>taskboard.sqlite</c> in the data dir is NOT picked up on startup — the
/// canonical <c>harness.sqlite</c> is created fresh and the legacy file is
/// left untouched (migration path: <c>install.sh --migrate</c>).
/// </summary>
public class HarnessHomeMigrationTests : IClassFixture<HarnessHomeMigrationTests.LegacyDbFactory>
{
    private readonly LegacyDbFactory _factory;

    public HarnessHomeMigrationTests(LegacyDbFactory factory)
    {
        _factory = factory;
    }

    public sealed class LegacyDbFactory : WebApplicationFactory<Program>
    {
        public string DataDir { get; } = Path.Join(Path.GetTempPath(), $"tb-migrate-{Guid.NewGuid()}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var name in new[]
            {
                "HARNESS_ADMIN_USERNAME", "HARNESS_ADMIN_PASSWORD",
                "TASKBOARD_ADMIN_USERNAME", "TASKBOARD_ADMIN_PASSWORD",
                "HARNESS_DATA_DIR", "TASKBOARD_DATA_DIR",
                "Harness__DataDir", "Taskboard__DataDir",
            })
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            builder.UseSetting("Taskboard:DataDir", DataDir);
            builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
        }
    }

    [Fact]
    public async Task Dado_BancoLegado_Quando_Boot_Entao_IgnoraECriaHarnessSqlite()
    {
        // Pre-seed the legacy database file before the host boots (the host is
        // created lazily on the first CreateClient call).
        Directory.CreateDirectory(_factory.DataDir);
        var legacyDb = Path.Join(_factory.DataDir, "taskboard.sqlite");
        var harnessDb = Path.Join(_factory.DataDir, "harness.sqlite");
        await File.WriteAllTextAsync(legacyDb, "sentinel");

        using var client = _factory.CreateClient();

        File.Exists(harnessDb).ShouldBeTrue("startup always works on harness.sqlite");
        File.Exists(legacyDb).ShouldBeTrue("the legacy file is left untouched");
        (await File.ReadAllTextAsync(legacyDb)).ShouldBe("sentinel");
    }
}
