using System.Runtime.InteropServices;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Localiza executáveis no PATH do sistema operacional.
/// </summary>
public static class PathSearch
{
    public static string? FindExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) && File.Exists(executable))
        {
            return executable;
        }

        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{executable}.exe"
            : executable;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';

        foreach (var directory in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
