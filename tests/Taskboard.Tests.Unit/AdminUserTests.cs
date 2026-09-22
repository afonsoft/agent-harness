using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;
using Taskboard.Server.Services;

namespace Taskboard.Tests.Unit;

public class AdminUserTests : IDisposable
{
    private readonly string _dataDir;

    public AdminUserTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"taskboard-admin-test-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_dataDir);

        Environment.SetEnvironmentVariable("HARNESS_ADMIN_USERNAME", "admin");
        Environment.SetEnvironmentVariable("HARNESS_ADMIN_PASSWORD", "Test123!");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }

        Environment.SetEnvironmentVariable("HARNESS_ADMIN_USERNAME", null);
        Environment.SetEnvironmentVariable("HARNESS_ADMIN_PASSWORD", null);
    }

    [Fact]
    public void Given_NoAdminFile_When_CreateFromConfiguration_Then_SeedsAndPersists()
    {
        var configuration = new ConfigurationBuilder().Build();

        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        user.Username.ShouldBe("admin");
        user.Validate("Test123!").ShouldBeTrue();
        File.Exists(Path.Combine(_dataDir, "admin.json")).ShouldBeTrue();
    }

    [Fact]
    public void Given_AdminFile_When_Load_Then_DoesNotReseed()
    {
        var configuration = new ConfigurationBuilder().Build();

        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);
        user.ChangePassword("New123!");

        var reloaded = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        reloaded.Username.ShouldBe("admin");
        reloaded.Validate("New123!").ShouldBeTrue();
    }

    [Fact]
    public void Given_AdminUser_When_ChangePassword_Then_NewPasswordValidAndPersisted()
    {
        var configuration = new ConfigurationBuilder().Build();
        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        user.ChangePassword("New123!");

        user.Validate("New123!").ShouldBeTrue();
        user.Validate("Test123!").ShouldBeFalse();
        var persisted = File.ReadAllText(Path.Combine(_dataDir, "admin.json"));
        persisted.ShouldContain(user.PasswordHash);
    }

    [Fact]
    public void Given_EnvAndConfigSet_When_CreateFromConfiguration_Then_EnvWins()
    {
        // SPEC-20260914-env-var-precedence RF-002: HARNESS_ADMIN_* env wins over Admin:* config
        Environment.SetEnvironmentVariable("HARNESS_ADMIN_USERNAME", "ops");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Admin:Username", "ignored"), new KeyValuePair<string, string?>("Admin:Password", "Ignored123!")])
            .Build();

        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        user.Username.ShouldBe("ops");
        user.Validate("Test123!").ShouldBeTrue();
        user.Validate("Ignored123!").ShouldBeFalse();
    }

    [Fact]
    public void Given_OnlyConfigSet_When_CreateFromConfiguration_Then_ConfigUsed()
    {
        Environment.SetEnvironmentVariable("HARNESS_ADMIN_USERNAME", null);
        Environment.SetEnvironmentVariable("HARNESS_ADMIN_PASSWORD", null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Admin:Username", "cfgadmin"), new KeyValuePair<string, string?>("Admin:Password", "Cfg123!")])
            .Build();

        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        user.Username.ShouldBe("cfgadmin");
        user.Validate("Cfg123!").ShouldBeTrue();
    }

    [Fact]
    public void Given_AdminUser_When_ValidateEmpty_Then_False()
    {
        var configuration = new ConfigurationBuilder().Build();
        var user = AdminUser.CreateFromConfiguration(configuration, _dataDir);

        user.Validate("").ShouldBeFalse();
        user.Validate(null).ShouldBeFalse();
    }
}
