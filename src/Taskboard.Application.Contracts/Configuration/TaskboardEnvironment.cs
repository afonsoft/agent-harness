using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Taskboard.Application.Contracts.Configuration;

/// <summary>
/// Resolves taskboard environment values from configuration and environment variables.
/// </summary>
/// <remarks>
/// This class is no longer static so it can be tested with mock configuration.
/// </remarks>
public sealed class TaskboardEnvironment
{
    private const int DefaultPort = 47823;
    private const string DefaultDataDirName = ".data";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;

    public TaskboardEnvironment(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
    }

    /// <summary>
    /// Returns the configured taskboard port. Defaults to <c>47823</c>.
    /// </summary>
    public int GetPort()
    {
        var configured = GetTrimmedOrDefault("TASKBOARD_PORT", string.Empty);
        if (string.IsNullOrEmpty(configured))
        {
            configured = _configuration["Taskboard:Port"];
        }

        if (int.TryParse(configured, out var port))
        {
            return port;
        }

        return DefaultPort;
    }

    /// <summary>
    /// Returns the configured taskboard data directory. Defaults to
    /// <c>{contentRoot}/.data</c>.
    /// </summary>
    public string GetDataDir()
    {
        var configured = GetTrimmedOrDefault("TASKBOARD_DATA_DIR", string.Empty);
        if (string.IsNullOrEmpty(configured))
        {
            configured = _configuration["Taskboard:DataDir"];
        }

        if (string.IsNullOrEmpty(configured))
        {
            return System.IO.Path.Combine(_hostEnvironment.ContentRootPath, DefaultDataDirName);
        }

        return configured;
    }

    /// <summary>
    /// Returns the server URLs. <c>ASPNETCORE_URLS</c> takes precedence;
    /// otherwise falls back to <c>http://127.0.0.1:{Taskboard:Port}</c>.
    /// </summary>
    public string GetServerUrls()
    {
        var aspNetCoreUrls = GetTrimmedOrDefault("ASPNETCORE_URLS", string.Empty);
        if (!string.IsNullOrEmpty(aspNetCoreUrls))
        {
            return aspNetCoreUrls;
        }

        return $"http://127.0.0.1:{GetPort()}";
    }

    private static string GetTrimmedOrDefault(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
