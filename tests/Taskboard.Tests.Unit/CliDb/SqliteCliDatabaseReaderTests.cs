using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Dtos;
using Taskboard.Integrations.CliDb;
using Xunit;

namespace Taskboard.Tests.Unit.CliDb;

public class SqliteCliDatabaseReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "clidb-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private CliDbSource Fonte() =>
        new("test", "x.db", ["sessions"], ["credential", "account"]);

    private SqliteCliDatabaseReader CriarReader(CliDbReadOptions? options = null) =>
        new(_home, NullLogger<SqliteCliDatabaseReader>.Instance, options ?? new CliDbReadOptions());

    private string CriarDb(string relPath, bool wal = false, SqliteConnection? keepOpen = null)
    {
        var path = Path.Combine(_home, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var conn = keepOpen ?? new SqliteConnection($"Data Source={path};Pooling=false");
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (wal)
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = "CREATE TABLE sessions (id TEXT PRIMARY KEY, title TEXT, token TEXT, started INTEGER);"
            + "CREATE TABLE credential (secret TEXT);"
            + "INSERT INTO sessions VALUES ('s1','first','should-hide',1000),('s2','second','x',2000);";
        cmd.ExecuteNonQuery();
        if (keepOpen is null)
        {
            conn.Dispose();
        }
        return path;
    }

    [Fact]
    public async Task Dado_BancoSimples_Quando_Query_Entao_LeLinhasSemCopiar()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        conn.CopiedToTemp.ShouldBeFalse();
        var rows = await conn.QueryAsync("sessions", ["id", "title"], r => r.GetString("id"));
        rows.ShouldBe(["s1", "s2"]);
    }

    [Fact]
    public async Task Dado_TabelaNegada_Quando_Query_Entao_RejeitaAntesDeExecutar()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        await Should.ThrowAsync<CliDbAccessDeniedException>(
            () => conn.QueryAsync("credential", ["secret"], r => r.GetString("secret")));
        await Should.ThrowAsync<CliDbAccessDeniedException>(
            () => conn.QueryAsync("sqlite_master", ["name"], r => r.GetString("name"))); // fora da whitelist
    }

    [Fact]
    public async Task Dado_ColunaComNomeDeSegredo_Quando_Query_Entao_ColunaExcluida()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        var rows = await conn.QueryAsync("sessions", ["id", "token"], r => (r.GetString("id"), r.GetString("token")));
        rows.ShouldAllBe(r => r.Item2 == null);
        rows.Select(r => r.Item1).ShouldBe(["s1", "s2"]);
    }

    [Fact]
    public async Task Dado_IdentificadorInvalido_Quando_Query_Entao_Rejeitado()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        await Should.ThrowAsync<CliDbAccessDeniedException>(
            () => conn.QueryAsync("sessions; DROP TABLE sessions", ["id"], r => r.GetString("id")));
        await Should.ThrowAsync<CliDbAccessDeniedException>(
            () => conn.QueryAsync("sessions", ["id FROM sessions"], r => r.GetString("id")));
    }

    [Fact]
    public async Task Dado_WhereParametrizado_Quando_Query_Entao_FiltraEOrdena()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        var rows = await conn.QueryAsync(
            "sessions", ["id", "started"],
            r => r.GetString("id"),
            whereClause: "started > @cursor",
            parameters: new Dictionary<string, object?> { ["@cursor"] = 1000L },
            orderBy: "started DESC");

        rows.ShouldBe(["s2"]);
    }

    [Fact]
    public async Task Dado_LimiteDeLinhas_Quando_Query_Entao_Trunca()
    {
        var path = CriarDb("x.db");
        await using var conn = await CriarReader().OpenAsync(Fonte(), path);

        var rows = await conn.QueryAsync("sessions", ["id"], r => r.GetString("id"), rowLimit: 1);
        rows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dado_BancoWalComEscritorVivo_Quando_Abrir_Entao_LeCopiaTempELimpa()
    {
        var writer = new SqliteConnection();
        string path;
        try
        {
            var dbPath = Path.Combine(_home, "wal.db");
            Directory.CreateDirectory(_home);
            writer = new SqliteConnection($"Data Source={dbPath};Pooling=false");
            path = CriarDb("wal.db", wal: true, keepOpen: writer);
            File.Exists(path + "-wal").ShouldBeTrue("fixture precisa de -wal presente");
        }
        catch
        {
            writer.Dispose();
            throw;
        }

        string? tempCopyDir = null;
        var reader = new SqliteCliDatabaseReader(
            _home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions(),
            onTempCopy: d => _tempDirs.Add(d));

        try
        {
            await using var conn = await reader.OpenAsync(Fonte(), path);

            conn.CopiedToTemp.ShouldBeTrue();
            var rows = await conn.QueryAsync("sessions", ["id"], r => r.GetString("id"));
            rows.ShouldContain("s1");
            tempCopyDir = _tempDirs.Single();
            Directory.Exists(tempCopyDir).ShouldBeTrue();

            // Nenhum journal criado no diretório da fonte.
            Directory.GetFiles(_home, "*.db-journal").ShouldBeEmpty();
        }
        finally
        {
            writer.Dispose();
        }

        await Task.Delay(50);
        Directory.Exists(tempCopyDir!).ShouldBeFalse("temp copy deve ser removida no dispose");
    }

    [Fact]
    public async Task Dado_ArquivoAcimaDoCap_Quando_Abrir_Entao_ErroSemAbrir()
    {
        var path = CriarDb("x.db");
        var reader = CriarReader(new CliDbReadOptions(MaxFileSizeBytes: 10));

        await Should.ThrowAsync<CliDbReadException>(() => reader.OpenAsync(Fonte(), path));
    }

    [Fact]
    public async Task Dado_ArquivoInexistente_Quando_Abrir_Entao_Erro()
    {
        await Should.ThrowAsync<CliDbReadException>(
            () => CriarReader().OpenAsync(Fonte(), Path.Combine(_home, "nope.db")));
    }

    [Fact]
    public async Task Dado_CaminhoForaDoHome_Quando_Abrir_Entao_AcessoNegado()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllBytes(outside, [1]);
        try
        {
            await Should.ThrowAsync<CliDbAccessDeniedException>(
                () => CriarReader().OpenAsync(Fonte(), outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Dado_ArquivoCorrompido_Quando_Abrir_Entao_ErroAposRetry()
    {
        var path = Path.Combine(_home, "corrupt.db");
        Directory.CreateDirectory(_home);
        await File.WriteAllTextAsync(path, "this is not a sqlite database at all, just text");

        await Should.ThrowAsync<CliDbReadException>(
            () => CriarReader().OpenAsync(Fonte(), path));
    }
}
