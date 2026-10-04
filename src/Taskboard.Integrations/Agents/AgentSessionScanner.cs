using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.CliDb;
using Taskboard.Integrations.CliDb.Extractors;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261006 RF-002 + SPEC-20261004-session-scanner-more-clis: scans each
/// CLI's on-disk session transcripts and builds the native resume command.
/// Read-only: file names, mtimes and session index fields (<c>sessionId</c>
/// head of a Gemini session file, agy's <c>history.jsonl</c> journal, devin's
/// <c>sessions</c> metadata columns) — never transcript message contents.
/// Missing/unreadable stores → empty, never errors.
/// </summary>
public sealed class AgentSessionScanner : IAgentSessionScanner
{
    /// <summary>Gemini keeps <c>sessionId</c> near the top of the session file.</summary>
    private const int GeminiHeadBytes = 8 * 1024;

    /// <summary>Cap for the agy session index journal — oversized files are skipped.</summary>
    private const long AgyHistoryMaxBytes = 8 * 1024 * 1024;

    private static readonly Regex GeminiSessionIdPattern =
        new("\"sessionId\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly string _home;

    public AgentSessionScanner()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal AgentSessionScanner(string home)
    {
        _home = home;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentSessionInfoDto>> ScanAsync(
        string? cli = null, int takePerCli = 50, CancellationToken ct = default)
    {
        var sessions = new List<AgentSessionInfoDto>();

        if (Include(cli, "claude"))
        {
            sessions.AddRange(ScanClaude(takePerCli));
        }

        if (Include(cli, "codex"))
        {
            sessions.AddRange(ScanCodex(takePerCli));
        }

        if (Include(cli, "opencode"))
        {
            sessions.AddRange(ScanOpenCode(takePerCli));
        }

        if (Include(cli, "gemini"))
        {
            sessions.AddRange(ScanGemini(takePerCli));
        }

        if (Include(cli, "agy", "antigravity"))
        {
            sessions.AddRange(ScanAntigravity(takePerCli));
        }

        if (Include(cli, "devin"))
        {
            sessions.AddRange(await ScanDevinAsync(takePerCli, ct).ConfigureAwait(false));
        }

        return sessions.OrderByDescending(s => s.ModifiedAtUtc).ToList();
    }

    private static bool Include(string? cli, params string[] names) =>
        string.IsNullOrWhiteSpace(cli)
        || names.Any(n => string.Equals(cli, n, StringComparison.OrdinalIgnoreCase));

    /// <summary>~/.claude/projects/&lt;slug&gt;/&lt;id&gt;.jsonl — slug is the cwd with separators flattened.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanClaude(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.Claude);
        var root = Path.Join(_home, ".claude", "projects");
        foreach (var file in NewestFiles(root, "*.jsonl", take))
        {
            var slug = Path.GetFileName(Path.GetDirectoryName(file)!) ?? string.Empty;
            var id = Path.GetFileNameWithoutExtension(file);
            yield return new AgentSessionInfoDto(
                "claude",
                id,
                DecodeSlug(slug),
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, id));
        }
    }

    /// <summary>~/.codex/sessions/**/rollout-*.jsonl — session id is the trailing uuid.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanCodex(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.Codex);
        var root = Path.Join(_home, ".codex", "sessions");
        foreach (var file in NewestFiles(root, "rollout-*.jsonl", take))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var sessionId = TrailingUuid(stem) ?? stem;
            yield return new AgentSessionInfoDto(
                "codex",
                sessionId,
                null,
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, sessionId));
        }
    }

    /// <summary>~/.local/share/opencode/**/storage/session/*.json — session ids like ses_*.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanOpenCode(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.OpenCode);
        var root = Path.Join(_home, ".local", "share", "opencode");
        var files = NewestFiles(root, "*.json", take * 4)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}session{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(f).StartsWith("ses_", StringComparison.Ordinal))
            .Take(take);
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            yield return new AgentSessionInfoDto(
                "opencode",
                id,
                null,
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, id));
        }
    }

    /// <summary>
    /// SPEC-20261004 RF-001: ~/.gemini/tmp/&lt;hash&gt;/chats/session-*.json —
    /// the cwd hash is opaque, so WorkingDirectory stays null. The session id
    /// is the <c>sessionId</c> field read from a bounded head of the file.
    /// Gemini CLI is not an <see cref="AgentCliKind"/> — the resume command is
    /// the documented <c>gemini --resume &lt;id&gt;</c> literal.
    /// </summary>
    private IEnumerable<AgentSessionInfoDto> ScanGemini(int take)
    {
        var tmp = Path.Join(_home, ".gemini", "tmp");
        var chatsSep = $"{Path.DirectorySeparatorChar}chats{Path.DirectorySeparatorChar}";
        var files = NewestFiles(tmp, "session-*.json", take * 2)
            .Where(f => f.Contains(chatsSep, StringComparison.Ordinal))
            .Take(take);
        foreach (var file in files)
        {
            var id = ReadGeminiSessionId(file) ?? Path.GetFileNameWithoutExtension(file);
            yield return new AgentSessionInfoDto(
                "gemini",
                id,
                null,
                File.GetLastWriteTimeUtc(file),
                file,
                $"gemini --resume {id}");
        }
    }

    /// <summary>
    /// SPEC-20261004 RF-002: ~/.gemini/antigravity-cli conversations, union of
    /// <c>brain/&lt;uuid&gt;/</c> directories and <c>conversations/&lt;uuid&gt;.db</c>
    /// stems. Workspace resolves from the <c>history.jsonl</c> index journal,
    /// falling back to <c>cache/last_conversations.json</c>.
    /// </summary>
    private IEnumerable<AgentSessionInfoDto> ScanAntigravity(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.Antigravity);
        var root = Path.Join(_home, ".gemini", "antigravity-cli");
        var workspaces = ReadAgyWorkspaces(Path.Join(root, "history.jsonl"), Path.Join(root, "cache", "last_conversations.json"));

        var byId = new Dictionary<string, (string Path, DateTime Mtime)>(StringComparer.Ordinal);
        var brain = Path.Join(root, "brain");
        if (Directory.Exists(brain))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(brain))
                {
                    var id = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    byId[id] = (dir, NewestMtime(dir));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // fall through to the conversations store
            }
        }

        var conversations = Path.Join(root, "conversations");
        foreach (var db in NewestFiles(conversations, "*.db", take))
        {
            var id = Path.GetFileNameWithoutExtension(db);
            if (!byId.ContainsKey(id))
            {
                byId[id] = (db, File.GetLastWriteTimeUtc(db));
            }
        }

        return byId
            .OrderByDescending(kv => kv.Value.Mtime)
            .Take(take)
            .Select(kv => new AgentSessionInfoDto(
                "agy",
                kv.Key,
                workspaces.GetValueOrDefault(kv.Key),
                kv.Value.Mtime,
                kv.Value.Path,
                ResumeCommand(spec, kv.Key)))
            .ToList();
    }

    /// <summary>
    /// SPEC-20261004 RF-003: ~/.local/share/devin/cli/sessions.db → sessions
    /// table via the read-only/WAL-safe CLI DB reader. Metadata columns only.
    /// </summary>
    private async Task<IReadOnlyList<AgentSessionInfoDto>> ScanDevinAsync(int take, CancellationToken ct)
    {
        var dbPath = Path.Join(_home, ".local", "share", "devin", "cli", "sessions.db");
        if (!File.Exists(dbPath))
        {
            return [];
        }

        var source = CliDatabaseMap.SourcesFor(AgentCliKind.Devin)
            .FirstOrDefault(s => s.WhitelistTables.Contains("sessions", StringComparer.Ordinal));
        if (source is null)
        {
            return [];
        }

        try
        {
            var reader = new SqliteCliDatabaseReader(_home, NullLogger<SqliteCliDatabaseReader>.Instance);
            await using var conn = await reader.OpenAsync(source, dbPath, ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync(
                "sessions",
                ["id", "title", "working_directory", "created_at", "last_activity_at"],
                r => (Id: r.GetString("id"),
                    Cwd: r.GetString("working_directory"),
                    ModifiedAt: CliDbTimestamps.OptEpochSeconds(r.GetInt64("last_activity_at"))
                        ?? CliDbTimestamps.EpochSeconds(r.GetInt64("created_at"))),
                orderBy: "last_activity_at DESC",
                rowLimit: take,
                cancellationToken: ct).ConfigureAwait(false);

            var spec = AgentCliMap.GetSpec(AgentCliKind.Devin);
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .OrderByDescending(r => r.ModifiedAt)
                .Select(r => new AgentSessionInfoDto(
                    "devin",
                    r.Id!,
                    r.Cwd,
                    r.ModifiedAt.UtcDateTime,
                    dbPath,
                    ResumeCommand(spec, r.Id!)))
                .ToList();
        }
        catch (Exception ex) when (ex is CliDbReadException or CliDbAccessDeniedException
            or SqliteException or IOException or UnauthorizedAccessException)
        {
            // Missing/drifted/locked DBs degrade to no devin sessions — never errors.
            return [];
        }
    }

    /// <summary>sessionId field from the first <see cref="GeminiHeadBytes"/> bytes of a session file.</summary>
    private static string? ReadGeminiSessionId(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[GeminiHeadBytes];
            var read = stream.Read(buffer, 0, buffer.Length);
            var head = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            var match = GeminiSessionIdPattern.Match(head);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// conversationId → workspace map: every line of history.jsonl wins over the
    /// previous one (append-only journal); last_conversations.json is the
    /// workspace→latest-conversation fallback for ids absent from history.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadAgyWorkspaces(string historyPath, string lastConversationsPath)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(historyPath) && new FileInfo(historyPath).Length <= AgyHistoryMaxBytes)
            {
                foreach (var line in File.ReadLines(historyPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("conversationId", out var idEl)
                            && root.TryGetProperty("workspace", out var wsEl)
                            && idEl.GetString() is { Length: > 0 } id
                            && wsEl.GetString() is { Length: > 0 } ws)
                        {
                            map[id] = ws;
                        }
                    }
                    catch (JsonException)
                    {
                        // torn final line — skip it, keep the rest
                    }
                }
            }

            if (File.Exists(lastConversationsPath) && new FileInfo(lastConversationsPath).Length <= AgyHistoryMaxBytes)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(lastConversationsPath));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.GetString() is { Length: > 0 } id && !map.ContainsKey(id))
                        {
                            map[id] = prop.Name;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // index unreadable → sessions still list, workspace stays null
        }

        return map;
    }

    /// <summary>Newest <c>*.jsonl</c> mtime under <paramref name="dir"/>, else the dir's own mtime.</summary>
    private static DateTime NewestMtime(string dir)
    {
        try
        {
            var newest = Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty()
                .Max();
            var dirTime = Directory.GetLastWriteTimeUtc(dir);
            return newest > dirTime ? newest : dirTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Directory.GetLastWriteTimeUtc(dir);
        }
    }

    /// <summary>Files of <paramref name="root"/> matching <paramref name="pattern"/>,
    /// newest-first, capped. Missing/unreadable trees → empty, never throws.</summary>
    private static IReadOnlyList<string> NewestFiles(string root, string pattern, int take)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(take)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Binary + native resume argv; null when the spec/cli has no resume.</summary>
    private static string? ResumeCommand(AgentCliSpec? spec, string sessionId) =>
        spec?.BuildResumeArgs(sessionId) is { } args
            ? $"{spec.Binary} {string.Join(' ', args)}"
            : null;

    /// <summary>Claude flattens the cwd into the project dir name (<c>-</c> for <c>/</c>).</summary>
    private static string? DecodeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var decoded = slug.Replace('-', '/');
        return decoded.StartsWith('/') ? decoded : $"/{decoded}";
    }

    /// <summary>Rollout file names end with the session uuid.</summary>
    private static string? TrailingUuid(string stem)
    {
        var tail = stem.Length > 36 ? stem[^36..] : stem;
        return Guid.TryParse(tail, out _) ? tail : null;
    }
}
