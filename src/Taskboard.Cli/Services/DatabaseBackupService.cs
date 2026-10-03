using Microsoft.Data.Sqlite;
using Taskboard.Domain.Shared.Configuration;

namespace Taskboard.Cli.Services;

/// <summary>
/// SPEC-20261003-sqlite-backup: online-consistent copy of harness.sqlite via
/// <c>VACUUM INTO</c> (single-transaction snapshot, safe while the server is
/// running), plus a guarded restore that refuses non-SQLite inputs and
/// requires --force before overwriting an existing DB.
/// </summary>
public static class DatabaseBackupService
{
    public const string BackupPrefix = "harness-backup-";
    public const string BackupExtension = ".sqlite";
    public const string DatabaseFileName = "harness.sqlite";

    private static readonly byte[] SqliteMagic =
        "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// DB path precedence: --db option &gt; HARNESS_DATA_DIR env &gt;
    /// the standard home ~/.agent-harness.
    /// </summary>
    public static string ResolveDatabasePath(string? dbOption)
    {
        if (!string.IsNullOrWhiteSpace(dbOption))
        {
            return Path.GetFullPath(dbOption);
        }

        var dataDir = HarnessEnv.Get("HARNESS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            return Path.Combine(dataDir, DatabaseFileName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agent-harness", DatabaseFileName);
    }

    /// <summary>
    /// Consistent copy of <paramref name="sourceDb"/> into
    /// <paramref name="destDir"/>/harness-backup-{utc}.sqlite, then prunes old
    /// backups beyond <paramref name="keep"/>. Returns the created path.
    /// </summary>
    public static string Backup(string sourceDb, string destDir, int keep = 7)
    {
        if (keep < 1)
        {
            throw new CliException(2, "--keep deve ser >= 1.");
        }

        if (!File.Exists(sourceDb))
        {
            throw new CliException(2, $"Banco não encontrado: {sourceDb}");
        }

        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, NextBackupFileName(destDir));

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourceDb,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $dest";
        command.Parameters.AddWithValue("$dest", dest);
        command.ExecuteNonQuery();

        Prune(destDir, keep);
        return dest;
    }

    /// <summary>
    /// Deletes the oldest harness-backup-*.sqlite files in <paramref name="dir"/>
    /// keeping the <paramref name="keep"/> newest (name sort == time sort).
    /// </summary>
    public static int Prune(string dir, int keep)
    {
        var backups = Directory
            .EnumerateFiles(dir, BackupPrefix + "*" + BackupExtension)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        foreach (var stale in backups)
        {
            File.Delete(stale);
        }

        return backups.Count;
    }

    /// <summary>
    /// Replaces <paramref name="targetDb"/> with <paramref name="sourceFile"/>
    /// after a SQLite header check. Overwriting an existing DB requires
    /// <paramref name="force"/>; stale -wal/-shm sidecars are removed so the
    /// restored file can't be paired with an old journal.
    /// </summary>
    public static string Restore(string sourceFile, string targetDb, bool force)
    {
        if (!File.Exists(sourceFile))
        {
            throw new CliException(2, $"Arquivo de backup não encontrado: {sourceFile}");
        }

        if (!IsSqliteFile(sourceFile))
        {
            throw new CliException(2, "O arquivo informado não é um banco SQLite válido.");
        }

        if (File.Exists(targetDb) && !force)
        {
            throw new CliException(3,
                $"O destino já existe: {targetDb} — pare o servidor e rode com --force para sobrescrever.");
        }

        File.Copy(sourceFile, targetDb, overwrite: true);
        File.Delete(targetDb + "-wal");
        File.Delete(targetDb + "-shm");
        return targetDb;
    }

    public static bool IsSqliteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var header = new byte[SqliteMagic.Length];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header, 0, header.Length);
        return read == SqliteMagic.Length && header.AsSpan().SequenceEqual(SqliteMagic);
    }

    private static string NextBackupFileName(string destDir)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var name = $"{BackupPrefix}{stamp}{BackupExtension}";
        for (var i = 2; File.Exists(Path.Combine(destDir, name)); i++)
        {
            name = $"{BackupPrefix}{stamp}-{i}{BackupExtension}";
        }

        return name;
    }
}
