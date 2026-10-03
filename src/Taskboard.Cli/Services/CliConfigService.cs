using System.Text.Json;

namespace Taskboard.Cli.Services;

public sealed class CliConfig
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:47823";
    public string? CurrentProject { get; set; }
    public string? CurrentWorkspace { get; set; }
    public string? CloudUrl { get; set; }
}

public static class CliConfigService
{
    private static string ConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            // Minimal containers: ApplicationData resolves empty when
            // XDG_CONFIG_HOME is unset — fall back to ~/.config.
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var dir = Path.Combine(appData, "taskctl");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static CliConfig Load()
    {
        var path = ConfigPath();
        if (!File.Exists(path))
        {
            return new CliConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CliConfig>(json, CliConfigContext.Options) ?? new CliConfig();
    }

    public static void Save(CliConfig config)
    {
        var path = ConfigPath();
        var json = JsonSerializer.Serialize(config, CliConfigContext.Options);
        File.WriteAllText(path, json);
    }
}

file static class CliConfigContext
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
