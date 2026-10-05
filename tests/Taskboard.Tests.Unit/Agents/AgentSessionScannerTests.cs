using Microsoft.Data.Sqlite;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261004-session-scanner-more-clis: gemini, agy and devin on-disk
/// session scanning (layouts, workspace resolution, resume commands).
/// </summary>
public class AgentSessionScannerTests : IDisposable
{
    private readonly string _root = Path.Join(
        Path.GetTempPath(), $"scanner-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Dado_SessaoGemini_Quando_Scan_Entao_ListaComResume()
    {
        var uuid = Guid.NewGuid().ToString();
        var chatsDir = Path.Join(_root, ".gemini", "tmp", "abc123hash", "chats");
        Directory.CreateDirectory(chatsDir);
        File.WriteAllText(
            Path.Join(chatsDir, "session-2026-10-04T10-00-00-abcdef.json"),
            $$"""{"sessionId": "{{uuid}}", "projectHash": "abc123hash", "messages": [{"x":1}]}""");

        var sessions = await new AgentSessionScanner(_root).ScanAsync();

        var gemini = sessions.Where(s => s.Cli == "gemini").ShouldHaveSingleItem();
        gemini.SessionId.ShouldBe(uuid);
        gemini.ResumeCommand.ShouldBe($"gemini --resume {uuid}");
        gemini.WorkingDirectory.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_BrainEHistoryAgy_Quando_Scan_Entao_ListaComWorkspace()
    {
        var uuid = Guid.NewGuid().ToString();
        var agyRoot = Path.Join(_root, ".gemini", "antigravity-cli");
        var logsDir = Path.Join(agyRoot, "brain", uuid, ".system_generated", "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Join(logsDir, "transcript.jsonl"), "{}");
        File.WriteAllText(
            Path.Join(agyRoot, "history.jsonl"),
            $$"""{"conversationId": "{{uuid}}", "workspace": "/home/user/repos/proj"}""" + "\n");

        var sessions = await new AgentSessionScanner(_root).ScanAsync();

        var agy = sessions.Where(s => s.Cli == "agy").ShouldHaveSingleItem();
        agy.SessionId.ShouldBe(uuid);
        agy.WorkingDirectory.ShouldBe("/home/user/repos/proj");
        agy.ResumeCommand.ShouldBe($"agy --conversation {uuid}");
    }

    [Fact]
    public async Task Dado_ConversationsDbAgy_Quando_Scan_Entao_Lista()
    {
        var uuid = Guid.NewGuid().ToString();
        var convDir = Path.Join(_root, ".gemini", "antigravity-cli", "conversations");
        Directory.CreateDirectory(convDir);
        var dbPath = Path.Join(convDir, $"{uuid}.db");
        File.WriteAllText(dbPath, "not-a-real-db");

        var sessions = await new AgentSessionScanner(_root).ScanAsync();

        var agy = sessions.Where(s => s.Cli == "agy").ShouldHaveSingleItem();
        agy.SessionId.ShouldBe(uuid);
        agy.SourcePath.ShouldBe(dbPath);
        agy.ResumeCommand.ShouldBe($"agy --conversation {uuid}");
    }

    [Fact]
    public async Task Dado_LastConversationsAgy_Quando_Scan_Entao_ResolveWorkspace()
    {
        var uuid = Guid.NewGuid().ToString();
        var agyRoot = Path.Join(_root, ".gemini", "antigravity-cli");
        Directory.CreateDirectory(Path.Join(agyRoot, "brain", uuid));
        var cacheDir = Path.Join(agyRoot, "cache");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(
            Path.Join(cacheDir, "last_conversations.json"),
            $$"""{"/home/user/repos/proj": "{{uuid}}"}""");

        var sessions = await new AgentSessionScanner(_root).ScanAsync();

        sessions.Where(s => s.Cli == "agy").ShouldHaveSingleItem()
            .WorkingDirectory.ShouldBe("/home/user/repos/proj");
    }

    [Fact]
    public async Task Dado_SessionsDbDevin_Quando_Scan_Entao_ListaComResume()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var cliDir = Path.Join(_root, ".local", "share", "devin", "cli");
        Directory.CreateDirectory(cliDir);
        var dbPath = Path.Join(cliDir, "sessions.db");
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            await Exec(conn, "CREATE TABLE sessions (id TEXT, title TEXT, created_at INTEGER, last_activity_at INTEGER, working_directory TEXT)");
            await Exec(conn,
                $"INSERT INTO sessions VALUES ('{id}', 't', 1759600000, 1759603600, '/home/user/repos/proj')");
        }

        var sessions = await new AgentSessionScanner(_root).ScanAsync();

        var devin = sessions.Where(s => s.Cli == "devin").ShouldHaveSingleItem();
        devin.SessionId.ShouldBe(id);
        devin.WorkingDirectory.ShouldBe("/home/user/repos/proj");
        devin.ModifiedAtUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1759603600).UtcDateTime);
        devin.ResumeCommand.ShouldBe($"devin --resume {id}");
    }

    [Fact]
    public async Task Dado_SessionsDbCorrompida_Quando_ScanDevin_Entao_VazioSemErro()
    {
        var cliDir = Path.Join(_root, ".local", "share", "devin", "cli");
        Directory.CreateDirectory(cliDir);
        File.WriteAllText(Path.Join(cliDir, "sessions.db"), "not sqlite at all");

        var sessions = await new AgentSessionScanner(_root).ScanAsync(cli: "devin");

        sessions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("agy")]
    [InlineData("antigravity")]
    public async Task Dado_FiltroAgy_Quando_Scan_Entao_SoAgy(string filter)
    {
        var uuid = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Join(_root, ".gemini", "antigravity-cli", "brain", uuid));
        var claudeDir = Path.Join(_root, ".claude", "projects", "slug");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Join(claudeDir, "a.jsonl"), "{}");

        var sessions = await new AgentSessionScanner(_root).ScanAsync(cli: filter);

        sessions.ShouldAllBe(s => s.Cli == "agy");
        sessions.ShouldHaveSingleItem().SessionId.ShouldBe(uuid);
    }

    [Fact]
    public void Dado_SpecsNovosResume_Quando_BuildResumeArgs_Entao_ArgvPorCli()
    {
        AgentCliMap.GetSpec(AgentCliKind.Devin)!.BuildResumeArgs("id1")
            .ShouldBe(["--resume", "id1"]);
        AgentCliMap.GetSpec(AgentCliKind.Antigravity)!.BuildResumeArgs("id1")
            .ShouldBe(["--conversation", "id1"]);
    }

    private static async Task Exec(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
