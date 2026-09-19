using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;
using Taskboard.Integrations.CliDb;
using Xunit;

namespace Taskboard.Tests.Unit.CliDb;

public class CliDbFingerprintTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "clidb-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static CliDbSource Fonte() =>
        new("test", "x.db", ["sessions"], ["credential"]);

    private string CriarDb(bool withEndedColumn = true)
    {
        var path = Path.Combine(_home, "x.db");
        Directory.CreateDirectory(_home);
        using var conn = new SqliteConnection($"Data Source={path};Pooling=false");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = withEndedColumn
            ? "CREATE TABLE sessions (id TEXT, title TEXT, started INTEGER, ended INTEGER);"
            : "CREATE TABLE sessions (id TEXT, title TEXT, started INTEGER);";
        cmd.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public async Task Dado_SchemaIgual_Quando_Fingerprint_Entao_AssinaturaEstavel()
    {
        var path = CriarDb();
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());

        await using var conn1 = await reader.OpenAsync(Fonte(), path);
        var fp1 = await conn1.GetSchemaFingerprintAsync(["sessions"]);
        await using var conn2 = await reader.OpenAsync(Fonte(), path);
        var fp2 = await conn2.GetSchemaFingerprintAsync(["sessions"]);

        fp1.ShouldBe(fp2);
        fp1.TablesSignature.ShouldContain("sessions(");
        fp1.TablesSignature.ShouldContain("id");
    }

    [Fact]
    public async Task Dado_ColunaRemovida_Quando_Fingerprint_Entao_Drift()
    {
        var path = CriarDb(withEndedColumn: true);
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());
        CliDbSchemaFingerprint baseline;
        await using (var conn = await reader.OpenAsync(Fonte(), path))
        {
            baseline = await conn.GetSchemaFingerprintAsync(["sessions"]);
        }

        // Recria o banco sem a coluna `ended`.
        File.Delete(path);
        CriarDb(withEndedColumn: false);

        await using var conn2 = await reader.OpenAsync(Fonte(), path);
        var atual = await conn2.GetSchemaFingerprintAsync(["sessions"]);

        CliDbSchemaFingerprinter.Matches(baseline, atual, out var diff).ShouldBeFalse();
        diff.ShouldContain("sessions");
        diff.ShouldContain("ended");
    }

    [Fact]
    public async Task Dado_TabelaRemovida_Quando_Fingerprint_Entao_Drift()
    {
        var path = CriarDb();
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());
        await using var conn = await reader.OpenAsync(Fonte(), path);
        var atual = await conn.GetSchemaFingerprintAsync(["sessions", "messages"]);

        var esperado = atual with { TablesSignature = atual.TablesSignature + "|messages(id)" };

        CliDbSchemaFingerprinter.Matches(esperado, atual, out var diff).ShouldBeFalse();
        diff.ShouldContain("messages");
    }

    [Fact]
    public async Task Dado_ExtractorComFingerprintEsperado_Quando_SchemaIgual_Entao_Extrai()
    {
        var path = CriarDb();
        var locator = new CliDatabaseLocator(_home, NullLogger<CliDatabaseLocator>.Instance);
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());
        CliDbSchemaFingerprint fp;
        await using (var conn = await reader.OpenAsync(Fonte(), path))
        {
            fp = await conn.GetSchemaFingerprintAsync(["sessions"]);
        }

        var extractor = new ExtractorStub(AgentCliKind.Codex, [Fonte()], fp, locator, reader);
        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        result.Reason.ShouldBeNull();
        extractor.ExtractedPaths.ShouldContain(path);
    }

    [Fact]
    public async Task Dado_ExtractorComFingerprintDefasado_Quando_SchemaDiverge_Entao_SchemaDriftedSemThrow()
    {
        var path = CriarDb();
        var locator = new CliDatabaseLocator(_home, NullLogger<CliDatabaseLocator>.Instance);
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());
        var defasado = new CliDbSchemaFingerprint(999, 999, "sessions(never,gonna)");

        var extractor = new ExtractorStub(AgentCliKind.Codex, [Fonte()], defasado, locator, reader);
        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.SchemaDrifted);
        result.Sessions.ShouldBeEmpty();
        result.Reason.ShouldNotBeNull();
        extractor.ExtractedPaths.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_BancoAusente_Quando_Extrair_Entao_Missing()
    {
        var locator = new CliDatabaseLocator(_home, NullLogger<CliDatabaseLocator>.Instance);
        var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());
        var extractor = new ExtractorStub(
            AgentCliKind.Codex, [Fonte()], new CliDbSchemaFingerprint(0, 0, ""), locator, reader);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Missing);
        result.Sessions.ShouldBeEmpty();
    }

    private sealed class ExtractorStub(
        AgentCliKind kind,
        IReadOnlyList<CliDbSource> sources,
        CliDbSchemaFingerprint expected,
        ICliDatabaseLocator locator,
        ICliDatabaseReader reader)
        : CliDbExtractorBase(locator, reader, NullLogger<ExtractorStub>.Instance)
    {
        public List<string> ExtractedPaths { get; } = [];

        public override AgentCliKind Kind => kind;
        public override CliDbSchemaFingerprint ExpectedFingerprint => expected;
        protected override IReadOnlyList<CliDbSource> Sources => sources;

        protected override Task<long?> ExtractSourceAsync(
            ICliDbConnection conn,
            string resolvedPath,
            CliDbSource source,
            long? rowCursor,
            List<CliSessionRecord> sessions,
            List<CliUsageRecord> usage,
            CancellationToken cancellationToken)
        {
            ExtractedPaths.Add(resolvedPath);
            return Task.FromResult<long?>(null);
        }
    }
}
