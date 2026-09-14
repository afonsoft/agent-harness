using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Configuration;
using Xunit;

namespace Taskboard.Tests.Unit.Configuration;

public class TaskboardEnvironmentTests
{
    private readonly IHostEnvironment _hostEnvironment;

    public TaskboardEnvironmentTests()
    {
        _hostEnvironment = Substitute.For<IHostEnvironment>();
        _hostEnvironment.ContentRootPath.Returns("/app");
    }

    private TaskboardEnvironment CreateSut(IConfiguration? configuration = null)
    {
        return new TaskboardEnvironment(configuration ?? new ConfigurationBuilder().Build(), _hostEnvironment);
    }

    [Fact]
    public void GetPort_WhenTaskboardPortIsSet_ShouldReturnConfiguredValue()
    {
        // Covers FR-003: port resolved from TASKBOARD_PORT env
        using var _ = SetEnv("TASKBOARD_PORT", "8080");

        var port = CreateSut().GetPort();

        port.ShouldBe(8080);
    }

    [Fact]
    public void GetPort_WhenTaskboardPortIsNotSet_ShouldReturnDefault()
    {
        // Covers FR-003: default port
        using var _ = ClearEnv("TASKBOARD_PORT");

        var port = CreateSut().GetPort();

        port.ShouldBe(47823);
    }

    [Fact]
    public void GetPort_WhenOnlyLegacyCodexPortIsSet_ShouldIgnoreAndReturnDefault()
    {
        // Covers breaking change: no fallback to CODEX_* variables
        using var _1 = ClearEnv("TASKBOARD_PORT");
        using var _2 = SetEnv("CODEX_TASKBOARD_PORT", "9000");

        var port = CreateSut().GetPort();

        port.ShouldBe(47823);
    }

    [Fact]
    public void GetDataDir_WhenTaskboardDataDirIsSet_ShouldReturnConfiguredValue()
    {
        // Covers FR-003: data dir resolved from TASKBOARD_DATA_DIR env
        using var _ = SetEnv("TASKBOARD_DATA_DIR", "/var/taskboard");

        var dataDir = CreateSut().GetDataDir();

        dataDir.ShouldBe("/var/taskboard");
    }

    [Fact]
    public void GetDataDir_WhenTaskboardDataDirIsNotSet_ShouldReturnDefaultUnderBasePath()
    {
        // Covers FR-003: default data dir under content root
        using var _ = ClearEnv("TASKBOARD_DATA_DIR");

        var dataDir = CreateSut().GetDataDir();

        dataDir.ShouldBe("/app/.data");
    }

    [Fact]
    public void GetDataDir_WhenOnlyLegacyCodexDataDirIsSet_ShouldIgnoreAndReturnDefault()
    {
        // Covers breaking change: no fallback to CODEX_* variables
        using var _1 = ClearEnv("TASKBOARD_DATA_DIR");
        using var _2 = SetEnv("CODEX_TASKBOARD_DATA_DIR", "/old");

        var dataDir = CreateSut().GetDataDir();

        dataDir.ShouldBe("/app/.data");
    }

    [Fact]
    public void GetServerUrls_WhenTaskboardPortIsSet_ShouldReturnLocalhostUrl()
    {
        // Covers FR-003: server URLs derive from TASKBOARD_PORT
        using var _ = SetEnv("TASKBOARD_PORT", "8080");

        var urls = CreateSut().GetServerUrls();

        urls.ShouldBe("http://127.0.0.1:8080");
    }

    [Fact]
    public void GetServerUrls_WhenAspNetCoreUrlsIsSet_ShouldReturnAspNetCoreUrls()
    {
        // Covers existing ASPNETCORE_URLS override behavior
        using var _ = SetEnv("ASPNETCORE_URLS", "http://0.0.0.0:5000");

        var urls = CreateSut().GetServerUrls();

        urls.ShouldBe("http://0.0.0.0:5000");
    }

    [Fact]
    public void GetPort_WhenConfiguredInAppSettings_ShouldReturnConfiguredValue()
    {
        // Covers FR-002: strongly-typed options override env
        using var _ = ClearEnv("TASKBOARD_PORT");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Taskboard:Port", "9090")])
            .Build();

        var port = CreateSut(configuration).GetPort();

        port.ShouldBe(9090);
    }

    [Fact]
    public void GetPort_WhenBothEnvAndConfigSet_ShouldPreferEnvVar()
    {
        // SPEC-20260914-env-var-precedence RF-001: TASKBOARD_* env wins over appsettings
        using var _ = SetEnv("TASKBOARD_PORT", "8080");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Taskboard:Port", "9090")])
            .Build();

        var port = CreateSut(configuration).GetPort();

        port.ShouldBe(8080);
    }

    [Fact]
    public void GetDataDir_WhenBothEnvAndConfigSet_ShouldPreferEnvVar()
    {
        // SPEC-20260914-env-var-precedence RF-001: TASKBOARD_* env wins over appsettings
        using var _ = SetEnv("TASKBOARD_DATA_DIR", "/srv/taskboard");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Taskboard:DataDir", "/ignored")])
            .Build();

        var dataDir = CreateSut(configuration).GetDataDir();

        dataDir.ShouldBe("/srv/taskboard");
    }

    private static IDisposable SetEnv(string name, string value)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestorer(name, original);
    }

    private static IDisposable ClearEnv(string name)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, null);
        return new EnvRestorer(name, original);
    }

    private sealed class EnvRestorer : IDisposable
    {
        private readonly string _name;
        private readonly string? _value;

        public EnvRestorer(string name, string? value)
        {
            _name = name;
            _value = value;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _value);
        }
    }
}
