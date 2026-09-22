using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Dtos;
using Taskboard.Integrations.CliDb;
using Taskboard.Integrations.CliDb.Extractors;
using Xunit;

namespace Taskboard.Tests.Unit.CliDb;

/// <summary>
/// Fixture DBs replicate the verified vendor schemas (2026-09-19) exactly —
/// column names only, all values anonymized.
/// </summary>
public class CliDbExtractorsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "clidb-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private CliDatabaseLocator Locator() => new(_home, NullLogger<CliDatabaseLocator>.Instance);
    private SqliteCliDatabaseReader Reader() =>
        new(_home, NullLogger<SqliteCliDatabaseReader>.Instance, new CliDbReadOptions());

    private string CriarDb(string relPath, string ddl)
    {
        var path = Path.Combine(_home, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var conn = new SqliteConnection($"Data Source={path};Pooling=false");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
        return path;
    }

    private const string OpenCodeDdl = """
        CREATE TABLE session (id TEXT, project_id TEXT, workspace_id TEXT, parent_id TEXT, slug TEXT,
            directory TEXT, path TEXT, title TEXT, version TEXT, share_url TEXT, summary_additions INTEGER,
            summary_deletions INTEGER, summary_files INTEGER, summary_diffs TEXT, metadata TEXT, cost REAL,
            tokens_input INTEGER, tokens_output INTEGER, tokens_reasoning INTEGER, tokens_cache_read INTEGER,
            tokens_cache_write INTEGER, revert TEXT, permission TEXT, agent TEXT, model TEXT,
            time_created INTEGER, time_updated INTEGER, time_compacting INTEGER, time_archived INTEGER);
        CREATE TABLE credential (secret TEXT);
        CREATE TABLE account (id TEXT);
        INSERT INTO session (id, title, model, time_created, time_updated, tokens_input, tokens_output,
            tokens_cache_read, tokens_cache_write, cost)
        VALUES ('oc-1','sessao um','gpt-x',1700000000000,1700000001000,10,20,3,4,0.5),
               ('oc-2','sessao dois','gpt-y',1700000002000,1700000003000,11,21,0,0,0.7);
        INSERT INTO credential VALUES ('SHOULD_NEVER_BE_READ');
        """;

    private const string CodexDdl = """
        CREATE TABLE threads (id TEXT, rollout_path TEXT, created_at INTEGER, updated_at INTEGER,
            source TEXT, model_provider TEXT, cwd TEXT, title TEXT, sandbox_policy TEXT, approval_mode TEXT,
            tokens_used INTEGER, has_user_event INTEGER, archived INTEGER, archived_at INTEGER, git_sha TEXT,
            git_branch TEXT, git_origin_url TEXT, cli_version TEXT, first_user_message TEXT,
            agent_nickname TEXT, agent_role TEXT, memory_mode TEXT, model TEXT, reasoning_effort TEXT,
            agent_path TEXT, created_at_ms INTEGER, daybreak_enabled INTEGER, history_mode TEXT,
            is_pinned INTEGER, name TEXT, originator TEXT, preview TEXT, project_id TEXT,
            recency_at INTEGER, recency_at_ms INTEGER, section_entered_at_ms INTEGER, section_position INTEGER,
            thread_section_id TEXT, thread_source TEXT, updated_at_ms INTEGER);
        INSERT INTO threads (id, title, created_at, updated_at, model, tokens_used)
        VALUES ('cx-1','thread um',1700000000,1700000100,'codex-m',38190);
        """;

    private const string DevinDdl = """
        CREATE TABLE sessions (id TEXT, working_directory TEXT, backend_type TEXT, model TEXT,
            agent_mode TEXT, created_at INTEGER, last_activity_at INTEGER, title TEXT, main_chain_id INTEGER,
            shell_last_seen_index INTEGER, cogs_json TEXT, workspace_dirs TEXT, hidden INTEGER, metadata TEXT);
        INSERT INTO sessions (id, title, created_at, last_activity_at, model, hidden)
        VALUES ('dv-1','sessao devin',1700000000,1700000500,'devin-m',0),
               ('dv-2','sessao sem nodes',1700000600,1700000700,'devin-m',0);
        CREATE TABLE message_nodes (row_id INTEGER PRIMARY KEY, session_id TEXT NOT NULL,
            node_id INTEGER NOT NULL, parent_node_id INTEGER, chat_message TEXT NOT NULL,
            created_at INTEGER NOT NULL, metadata TEXT);
        INSERT INTO message_nodes (session_id, node_id, chat_message, created_at)
        VALUES ('dv-1',1,'SEGREDO-QUE-NUNCA-DEVE-SAIR-DO-BANCO-0123456789',1700000100),
               ('dv-1',2,'outra mensagem qualquer',1700000200);
        """;

    private const string AntigravityDdl = """
        PRAGMA user_version=3;
        CREATE TABLE conversation_summaries (conversation_id TEXT, title TEXT, preview TEXT,
            step_count INTEGER, last_modified_time TEXT, workspace_uris TEXT, status TEXT, source TEXT,
            project_id TEXT, agent_name TEXT, parent_conversation_id TEXT, nesting_depth INTEGER,
            battle_id TEXT, winning_conversation_id TEXT, not_fully_idle INTEGER, killed INTEGER,
            last_user_input_time TEXT, last_user_input_step_index INTEGER, app_data_dir TEXT,
            raw_summary BLOB, group_id TEXT);
        INSERT INTO conversation_summaries (conversation_id, title, preview, step_count,
            last_modified_time, raw_summary)
        VALUES ('ag-1','conv agy','prev-1',7,'2026-09-19 10:00:00+00:00',zeroblob(8));
        """;

    private const string ClineDdl = """
        CREATE TABLE hub_events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, event TEXT,
            session_id TEXT, envelope_json TEXT, created_at INTEGER);
        INSERT INTO hub_events (sequence, event, session_id, envelope_json, created_at)
        VALUES (1,'e','cl-1','{}',1789336066864),(2,'e','cl-1','{}',1789336166864),(3,'e','cl-2','{}',1789336266864);
        """;

    private const string ClaudeCtxDdl = """
        CREATE TABLE session_meta (session_id TEXT, project_dir TEXT, started_at TEXT, last_event_at TEXT,
            event_count INTEGER, compact_count INTEGER, usage_cursor TEXT);
        INSERT INTO session_meta (session_id, project_dir, started_at, last_event_at, event_count)
        VALUES ('cc-1','/repo','2026-09-19 09:00:00','2026-09-19 09:30:00',42);
        """;

    [Fact]
    public async Task Dado_OpenCodeDb_Quando_Extrair_Entao_SessionsEUsoSemCredenciais()
    {
        CriarDb(".local/share/opencode/opencode.db", OpenCodeDdl);
        var extractor = new OpenCodeCliDbExtractor(
            Locator(), Reader(), NullLogger<OpenCodeCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        result.Sessions.Count.ShouldBe(2);
        result.Sessions.ShouldContain(s => s.ExternalId == "oc-1" && s.Title == "sessao um"
            && s.ModelName == "gpt-x" && s.TokensInput == 10 && s.TokensOutput == 20 && s.TokensCached == 7);
        result.Usage.ShouldContain(u => u.ExternalId == "oc-2" && u.CostUsd == 0.7m);
        result.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_Cursor_Quando_ExtrairDuasVezes_Entao_ResultadosIdenticos()
    {
        CriarDb(".local/share/opencode/opencode.db", OpenCodeDdl);
        var extractor = new OpenCodeCliDbExtractor(
            Locator(), Reader(), NullLogger<OpenCodeCliDbExtractor>.Instance);

        var first = await extractor.ExtractSinceAsync(null);
        var again = await extractor.ExtractSinceAsync(first.NextCursor);
        var repeat = await extractor.ExtractSinceAsync(first.NextCursor);

        again.Sessions.ShouldBeEmpty();
        again.Sessions.Select(s => s.ExternalId)
            .ShouldBe(repeat.Sessions.Select(s => s.ExternalId).ToList());
    }

    [Fact]
    public async Task Dado_CodexDb_Quando_Extrair_Entao_SessionsDeThreads()
    {
        CriarDb(".codex/state_5.sqlite", CodexDdl);
        var extractor = new CodexCliDbExtractor(
            Locator(), Reader(), NullLogger<CodexCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        var session = result.Sessions.ShouldHaveSingleItem();
        session.ExternalId.ShouldBe("cx-1");
        // SPEC-20260922 RF-002: tokens_used (vendor total) → TokensInput estimado.
        session.TokensInput.ShouldBe(38190);
        session.TokensEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DevinDb_Quando_Extrair_Entao_SessionsComEstimativaDeNodes()
    {
        CriarDb(".local/share/devin/cli/sessions.db", DevinDdl);
        var extractor = new DevinCliDbExtractor(
            Locator(), Reader(), NullLogger<DevinCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        result.Sessions.Count.ShouldBe(2);

        var com = result.Sessions.Single(s => s.ExternalId == "dv-1");
        com.StartedAtUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        com.EndedAtUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1700000500));
        // chat_message lengths: 47 + 23 = 70 chars → ceil(70/4) = 18 tokens.
        com.MessageCount.ShouldBe(2);
        com.TokensInput.ShouldBe(18);
        com.TokensEstimated.ShouldBeTrue();

        // Sem nodes → tokens null (badge "no usage data"), não zero real.
        var sem = result.Sessions.Single(s => s.ExternalId == "dv-2");
        sem.TokensInput.ShouldBeNull();
        sem.TokensEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DevinDb_Quando_Extrair_Entao_ConteudoDeMensagemNuncaSai()
    {
        CriarDb(".local/share/devin/cli/sessions.db", DevinDdl);
        var extractor = new DevinCliDbExtractor(
            Locator(), Reader(), NullLogger<DevinCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        const string payload = "SEGREDO-QUE-NUNCA-DEVE-SAIR-DO-BANCO";
        result.Sessions.ShouldAllBe(s =>
            (s.Title ?? "").Contains(payload, StringComparison.Ordinal) == false
            && s.ExternalId.Contains(payload, StringComparison.Ordinal) == false
            && (s.ModelName ?? "").Contains(payload, StringComparison.Ordinal) == false,
            customMessage: "somente scalars length()/count saem do banco do vendor");
    }

    [Fact]
    public async Task Dado_DevinDbSemMessageNodes_Quando_Extrair_Entao_SessionsSemTokens()
    {
        // message_nodes ausente/driftado não pode derrubar a extração de sessions.
        CriarDb(".local/share/devin/cli/sessions.db", """
            CREATE TABLE sessions (id TEXT, working_directory TEXT, backend_type TEXT, model TEXT,
                agent_mode TEXT, created_at INTEGER, last_activity_at INTEGER, title TEXT,
                main_chain_id INTEGER, shell_last_seen_index INTEGER, cogs_json TEXT,
                workspace_dirs TEXT, hidden INTEGER, metadata TEXT);
            INSERT INTO sessions (id, title, created_at, last_activity_at, model, hidden)
            VALUES ('dv-9','sessao solo',1700000000,1700000500,'devin-m',0);
            """);
        var extractor = new DevinCliDbExtractor(
            Locator(), Reader(), NullLogger<DevinCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        var session = result.Sessions.ShouldHaveSingleItem();
        session.ExternalId.ShouldBe("dv-9");
        session.TokensInput.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_AntigravityDb_Quando_Extrair_Entao_ApenasSummariesComEstimativa()
    {
        CriarDb(".gemini/antigravity-cli/conversation_summaries.db", AntigravityDdl);
        CriarDb(".gemini/antigravity-cli/conversations/aaaa.db", "CREATE TABLE steps (id TEXT);");
        var extractor = new AntigravityCliDbExtractor(
            Locator(), Reader(), NullLogger<AntigravityCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        var session = result.Sessions.ShouldHaveSingleItem();
        session.ExternalId.ShouldBe("ag-1");
        session.MessageCount.ShouldBe(7);
        // title(8) + preview(6) + raw_summary blob(8) = 22 chars → ceil(22/4) = 6.
        session.TokensInput.ShouldBe(6);
        session.TokensEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ClineDb_Quando_Extrair_Entao_SessionsAgrupadas()
    {
        CriarDb(".cline/data/db/hub-events-hub-production.db", ClineDdl);
        var extractor = new ClineCliDbExtractor(
            Locator(), Reader(), NullLogger<ClineCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        result.Sessions.Count.ShouldBe(2);
        // '{}' = 2 chars por envelope → cl-1: 4 chars → 1 token; cl-2: 2 → 1.
        result.Sessions.ShouldContain(s => s.ExternalId == "cl-1" && s.MessageCount == 2 && s.TokensInput == 1);
        result.Sessions.ShouldContain(s => s.ExternalId == "cl-2" && s.MessageCount == 1 && s.TokensInput == 1);
        result.Sessions.ShouldAllBe(s => s.StartedAtUtc.Year == 2026 && s.TokensEstimated,
            customMessage: "created_at é epoch milissegundos (regressão: era lido como segundos)");
    }

    [Fact]
    public async Task Dado_ClineDbSemSessionId_Quando_Extrair_Entao_CursorAvanca()
    {
        // Linhas sem session_id não geram sessão, mas devem mover o cursor —
        // senão todo sync re-lê o mesmo batch para sempre (bug real em produção).
        CriarDb(".cline/data/db/hub-events-hub-production.db", """
            CREATE TABLE hub_events (sequence INTEGER PRIMARY KEY AUTOINCREMENT, event TEXT,
                session_id TEXT, envelope_json TEXT, created_at INTEGER);
            INSERT INTO hub_events (event, session_id, envelope_json, created_at)
            VALUES ('e',NULL,'{}',1789336066864),('e','','{}',1789336166864),('e',NULL,'{}',1789336266864);
            """);
        var extractor = new ClineCliDbExtractor(
            Locator(), Reader(), NullLogger<ClineCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Sessions.ShouldBeEmpty();
        result.NextCursor.ShouldNotBeNull().ShouldEndWith("|3",
            customMessage: "cursor deve avançar pela última rowid escaneada");

        var next = await extractor.ExtractSinceAsync(result.NextCursor);
        next.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ClaudeCtxDb_Quando_Extrair_Entao_SessionsDoPlugin()
    {
        CriarDb(".claude/context-mode/sessions/abc.db", ClaudeCtxDdl);
        var extractor = new ClaudeContextModeCliDbExtractor(
            Locator(), Reader(), NullLogger<ClaudeContextModeCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.Available);
        var session = result.Sessions.ShouldHaveSingleItem();
        session.ExternalId.ShouldBe("cc-1");
        session.MessageCount.ShouldBe(42);
        // RF-002: event_count × EstimatedTokensPerEvent (default 1000).
        session.TokensInput.ShouldBe(42_000);
        session.TokensEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_FingerprintDoExtractor_Quando_CompararComFixture_Entao_SchemaCoberto()
    {
        // Cada fixture replica exatamente o baseline — drift significa schema real mudou.
        var checks = new (string RelPath, string Ddl, CliDbExtractorBase Extractor)[]
        {
            (".codex/state_1.sqlite", CodexDdl, new CodexCliDbExtractor(Locator(), Reader(), NullLogger<CodexCliDbExtractor>.Instance)),
            (".local/share/opencode/opencode.db", OpenCodeDdl, new OpenCodeCliDbExtractor(Locator(), Reader(), NullLogger<OpenCodeCliDbExtractor>.Instance)),
            (".local/share/devin/cli/sessions.db", DevinDdl, new DevinCliDbExtractor(Locator(), Reader(), NullLogger<DevinCliDbExtractor>.Instance)),
            (".gemini/antigravity-cli/conversation_summaries.db", AntigravityDdl, new AntigravityCliDbExtractor(Locator(), Reader(), NullLogger<AntigravityCliDbExtractor>.Instance)),
            (".cline/data/db/hub-events-x.db", ClineDdl, new ClineCliDbExtractor(Locator(), Reader(), NullLogger<ClineCliDbExtractor>.Instance)),
            (".claude/context-mode/sessions/z.db", ClaudeCtxDdl, new ClaudeContextModeCliDbExtractor(Locator(), Reader(), NullLogger<ClaudeContextModeCliDbExtractor>.Instance)),
        };

        foreach (var (relPath, ddl, extractor) in checks)
        {
            var path = CriarDb(relPath, ddl);
            var source = CliDatabaseMap.SourcesFor(extractor.Kind)
                .First(s => s.Name == extractor.ExpectedFingerprintSourceName());
            await using var conn = await Reader().OpenAsync(source, path);
            // Drift-checked tables only — estimation-only surfaces (Devin
            // message_nodes) ficam fora do gate de drift (SPEC-20260922).
            var fp = await conn.GetSchemaFingerprintAsync(extractor.DriftCheckedTables(source));
            CliDbSchemaFingerprinter.Matches(extractor.ExpectedFingerprint, fp, out var diff)
                .ShouldBeTrue(customMessage: $"{extractor.Kind} driftou: {diff}");
        }
    }

    [Fact]
    public async Task Dado_SchemaSemColuna_Quando_Extrair_Entao_SchemaDrifted()
    {
        CriarDb(".local/share/opencode/opencode.db",
            "CREATE TABLE session (id TEXT, title TEXT);");
        var extractor = new OpenCodeCliDbExtractor(
            Locator(), Reader(), NullLogger<OpenCodeCliDbExtractor>.Instance);

        var result = await extractor.ExtractSinceAsync(null);

        result.Status.ShouldBe(CliDbSourceStatus.SchemaDrifted);
        result.Sessions.ShouldBeEmpty();
    }
}

internal static class ExtractorTestHelpers
{
    /// <summary>Whitelisted table of the extractor's fingerprinted source.</summary>
    public static string ExpectedFingerprintSourceName(this CliDbExtractorBase extractor) =>
        extractor.Kind switch
        {
            AgentCliKind.Codex => "codex-state",
            AgentCliKind.OpenCode => "opencode-db",
            AgentCliKind.Devin => "devin-sessions",
            AgentCliKind.Antigravity => "antigravity-summaries",
            AgentCliKind.Cline => "cline-hub-events",
            AgentCliKind.Claude => "claude-context-mode",
            _ => throw new InvalidOperationException(),
        };
}
