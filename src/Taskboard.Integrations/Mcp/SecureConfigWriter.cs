namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Atomic, permission-restricted writes for files that may contain secrets
/// (SPEC-20260917-rag-mcp-provisioning RF-009): writes to a temp file then
/// renames over the target, leaves a <c>.bak</c> copy of the previous content,
/// and applies <c>0600</c> on POSIX.
/// </summary>
internal static class SecureConfigWriter
{
    internal static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
            TryRestrictPermissions(path + ".bak");
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        TryRestrictPermissions(path);
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best effort — some filesystems do not support POSIX modes.
        }
    }
}
