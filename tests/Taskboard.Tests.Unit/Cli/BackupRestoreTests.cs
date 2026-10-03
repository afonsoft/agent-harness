using Microsoft.Data.Sqlite;
using Shouldly;
using Spectre.Console.Cli.Testing;
using Taskboard.Cli.Services;
using Xunit;
using CliProgram = Taskboard.Cli.Program;

namespace Taskboard.Tests.Unit.Cli;

/// <summary>
/// SPEC-20261003-sqlite-backup: taskctl backup (VACUUM INTO online copy +
/// --keep retention) and taskctl restore (header check + --force guard).
/// </summary>
public sealed class BackupRestoreTests : IDisposable
{
    private readonly string _root;

    public BackupRestoreTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-backup-{Guid.NewGuid()}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CommandAppTester CriarTester()
    {
        var tester = new CommandAppTester();
        tester.Configure(CliProgram.ConfigureCommands);
        return tester;
    }

    private string CriarDb(string nome = "harness.sqlite")
    {
        var path = Path.Join(_root, nome);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE demo (id INTEGER PRIMARY KEY, v TEXT); INSERT INTO demo (v) VALUES ('x');";
        command.ExecuteNonQuery();
        return path;
    }

    private static int ContarLinhas(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM demo";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public async Task Dado_DbValido_Quando_Backup_Entao_CriaCopiaConsistente()
    {
        var db = CriarDb();
        var dest = Path.Join(_root, "backups");
        var tester = CriarTester();

        var result = await tester.RunAsync(["backup", dest, "--db", db]);

        result.ExitCode.ShouldBe(0);
        var files = Directory.GetFiles(dest, "harness-backup-*.sqlite");
        files.Length.ShouldBe(1);
        DatabaseBackupService.IsSqliteFile(files[0]).ShouldBeTrue();
        ContarLinhas(files[0]).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_DbInexistente_Quando_Backup_Entao_ExitCode2()
    {
        var tester = CriarTester();

        var result = await tester.RunAsync(["backup", Path.Join(_root, "b"), "--db", Path.Join(_root, "nope.sqlite")]);

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_KeepInvalido_Quando_Backup_Entao_ExitCode2()
    {
        var db = CriarDb();
        var tester = CriarTester();

        var result = await tester.RunAsync(["backup", Path.Join(_root, "b"), "--db", db, "--keep", "0"]);

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public void Dado_BackupsExcedentes_Quando_Prune_Entao_ApagaOsMaisAntigos()
    {
        var dir = Path.Join(_root, "bk");
        Directory.CreateDirectory(dir);
        for (var i = 1; i <= 8; i++)
        {
            File.WriteAllText(Path.Join(dir, $"harness-backup-2026100{i}-000000.sqlite"), "x");
        }

        File.WriteAllText(Path.Join(dir, "outro-arquivo.sqlite"), "keep-me");

        var deleted = DatabaseBackupService.Prune(dir, keep: 7);

        deleted.ShouldBe(1);
        Directory.GetFiles(dir, "harness-backup-*.sqlite").Length.ShouldBe(7);
        File.Exists(Path.Join(dir, "outro-arquivo.sqlite")).ShouldBeTrue();
        File.Exists(Path.Join(dir, "harness-backup-20261001-000000.sqlite")).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ArquivoNaoSqlite_Quando_Restore_Entao_RecusaComExitCode2()
    {
        var fake = Path.Join(_root, "fake.sqlite");
        File.WriteAllText(fake, "isto não é sqlite");
        var tester = CriarTester();

        var result = await tester.RunAsync(["restore", fake, "--db", Path.Join(_root, "dest.sqlite")]);

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_DestinoInexistente_Quando_Restore_Entao_RestauraSemForce()
    {
        var db = CriarDb();
        var backup = DatabaseBackupService.Backup(db, Path.Join(_root, "bk"));
        var dest = Path.Join(_root, "restore-target.sqlite");
        var tester = CriarTester();

        var result = await tester.RunAsync(["restore", backup, "--db", dest]);

        result.ExitCode.ShouldBe(0);
        ContarLinhas(dest).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_DestinoExistente_Quando_RestoreSemForce_Entao_ExitCode3()
    {
        var db = CriarDb();
        var backup = DatabaseBackupService.Backup(db, Path.Join(_root, "bk"));
        var dest = CriarDb("dest.sqlite");
        var tester = CriarTester();

        var result = await tester.RunAsync(["restore", backup, "--db", dest]);

        result.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task Dado_DestinoExistente_Quando_RestoreComForce_Entao_Sobrescreve()
    {
        var db = CriarDb();
        var backup = DatabaseBackupService.Backup(db, Path.Join(_root, "bk"));
        var dest = CriarDb("dest.sqlite");
        File.WriteAllText(dest + "-wal", "stale-journal");
        var tester = CriarTester();

        var result = await tester.RunAsync(["restore", backup, "--db", dest, "--force"]);

        result.ExitCode.ShouldBe(0);
        ContarLinhas(dest).ShouldBe(1);
        File.Exists(dest + "-wal").ShouldBeFalse();
    }
}
