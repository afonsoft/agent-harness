using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-001/RF-002/RF-005/RF-006: enriched
/// session index on top of <see cref="IAgentSessionScanner"/> — derived title,
/// git repo/branch (2s timeout, cached per cwd), size, message count — then
/// group/sort/filter with per-CLI visibility + hideEmpty.
/// Singleton; index rebuilds at most every 60s.
/// </summary>
public sealed class AgentSessionIndexService : IAgentSessionIndexService
{
    private const int TranscriptHeadBytes = 64 * 1024;
    private static readonly TimeSpan IndexTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GitCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly Regex AnsiPattern =
        new("\x1B\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex MarkdownNoise =
        new("[*_`#>\\[\\]]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex WhitespaceRun =
        new("\\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>CLIs with a native-chat (ACP) adapter — RF §6.1.</summary>
    private static readonly HashSet<string> NativeChatClis =
        new(StringComparer.Ordinal) { "claude", "codex", "opencode" };

    private readonly IServiceProvider _services;
    private readonly IAgentSessionScanner _scanner;
    private readonly IAgentSessionGitProbe _gitProbe;
    private readonly ILogger<AgentSessionIndexService>? _logger;
    private readonly TimeProvider _time;

    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private IReadOnlyList<SessionIndexEntryDto>? _index;
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private readonly ConcurrentDictionary<string, (string? RepoName, string? Branch, DateTimeOffset At)> _gitCache = new();

    public AgentSessionIndexService(
        IServiceProvider services,
        IAgentSessionScanner scanner,
        IAgentSessionGitProbe gitProbe,
        TimeProvider? timeProvider = null,
        ILogger<AgentSessionIndexService>? logger = null)
    {
        _services = services;
        _scanner = scanner;
        _gitProbe = gitProbe;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public void Invalidate() => _builtAt = DateTimeOffset.MinValue;

    public async Task<AgentSessionsIndexDto> GetAsync(
        string userId,
        string? cli = null,
        string group = "cli",
        string sort = "modified",
        string? q = null,
        bool? hideEmpty = null,
        CancellationToken ct = default)
    {
        var index = await EnsureIndexAsync(ct).ConfigureAwait(false);
        var prefs = await GetViewPrefsAsync(userId, ct).ConfigureAwait(false);
        var hidden = prefs.HiddenClis.ToHashSet(StringComparer.Ordinal);
        var collapseEmpty = hideEmpty ?? prefs.HideEmpty;

        IEnumerable<SessionIndexEntryDto> visible = index;
        if (!string.IsNullOrWhiteSpace(cli))
        {
            visible = visible.Where(e => string.Equals(e.Cli, cli, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            visible = visible.Where(e =>
                e.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (e.Cwd?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.Branch?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.RepoName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var filtered = visible.ToList();
        // RF-005: chips cover every supported CLI (even with zero sessions)
        // plus any non-canonical slugs the scan surfaced.
        var known = _scanner.SupportedClis
            .Concat(index.Select(e => e.Cli))
            .Concat(hidden)
            .Distinct(StringComparer.Ordinal);
        var chips = known
            .Select(c => new SessionCliChipDto(
                c, index.Count(e => e.Cli == c), hidden.Contains(c)))
            .OrderBy(c => c.Cli, StringComparer.Ordinal)
            .ToList();

        var rows = filtered.Where(e => !hidden.Contains(e.Cli)).ToList();
        var groups = GroupRows(rows, group)
            .Select(g => new SessionGroupDto(g.Key, g.Label, g.Rows.Count, SortRows(g.Rows, sort)));
        if (string.Equals(group, "cli", StringComparison.Ordinal))
        {
            // RF-005/RF-009: one group per visible chip so empty CLIs keep a
            // header when hideEmpty is off.
            var keys = chips.Where(c => !c.Hidden).Select(c => c.Cli);
            var byKey = groups.ToDictionary(g => g.Key, StringComparer.Ordinal);
            groups = keys.Select(k => byKey.TryGetValue(k, out var g)
                ? g
                : new SessionGroupDto(k, k, 0, []));
        }

        var result = groups
            .Where(g => g.Count > 0 || !collapseEmpty)
            .OrderByDescending(g => g.Count > 0)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentSessionsIndexDto(result, rows.Count, chips, collapseEmpty);
    }

    public async Task<SessionViewPrefsDto> GetViewPrefsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var visibility = scope.ServiceProvider
            .GetRequiredService<ISessionCliVisibilityRepository>();
        var users = scope.ServiceProvider
            .GetRequiredService<IRepository<UserPreference>>();
        var hidden = await visibility.ListHiddenAsync(userId, ct).ConfigureAwait(false);
        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return new SessionViewPrefsDto(user?.HideEmptySessionGroups ?? true, hidden);
    }

    public async Task<SessionViewPrefsDto> SaveViewPrefsAsync(
        string userId, SessionViewPrefsDto prefs, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var visibility = scope.ServiceProvider
            .GetRequiredService<ISessionCliVisibilityRepository>();
        var users = scope.ServiceProvider
            .GetRequiredService<IRepository<UserPreference>>();

        var current = await visibility.ListHiddenAsync(userId, ct).ConfigureAwait(false);
        var wanted = prefs.HiddenClis.ToHashSet(StringComparer.Ordinal);
        foreach (var cli in wanted.Except(current))
        {
            await visibility.SetHiddenAsync(userId, cli, true, ct).ConfigureAwait(false);
        }

        foreach (var cli in current.Where(c => !wanted.Contains(c)))
        {
            await visibility.SetHiddenAsync(userId, cli, false, ct).ConfigureAwait(false);
        }

        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        if (user is null)
        {
            await users.AddAsync(
                new UserPreference(Guid.Empty)
                {
                    HideEmptySessionGroups = prefs.HideEmpty,
                },
                ct).ConfigureAwait(false);
        }
        else
        {
            user.HideEmptySessionGroups = prefs.HideEmpty;
            await users.UpdateAsync(user, ct).ConfigureAwait(false);
        }

        return await GetViewPrefsAsync(userId, ct).ConfigureAwait(false);
    }

    // ---- index build ----

    private async Task<IReadOnlyList<SessionIndexEntryDto>> EnsureIndexAsync(CancellationToken ct)
    {
        if (_index is not null && _time.GetUtcNow() - _builtAt < IndexTtl)
        {
            return _index;
        }

        await _rebuildGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_index is not null && _time.GetUtcNow() - _builtAt < IndexTtl)
            {
                return _index;
            }

            var raw = await _scanner.ScanAsync(cli: null, takePerCli: 100, ct).ConfigureAwait(false);
            var entries = new List<SessionIndexEntryDto>(raw.Count);
            foreach (var s in raw)
            {
                entries.Add(await EnrichAsync(s, ct).ConfigureAwait(false));
            }

            _index = entries;
            _builtAt = _time.GetUtcNow();
            return entries;
        }
        finally
        {
            _rebuildGate.Release();
        }
    }

    private async Task<SessionIndexEntryDto> EnrichAsync(
        AgentSessionInfoDto s, CancellationToken ct)
    {
        long size = 0;
        DateTime created = s.ModifiedAtUtc;
        int? messages = null;
        string title = $"session-{ShortId(s.SessionId)}";
        try
        {
            var info = new FileInfo(s.SourcePath);
            if (info.Exists)
            {
                size = info.Length;
                created = info.CreationTimeUtc < s.ModifiedAtUtc
                    ? info.CreationTimeUtc
                    : s.ModifiedAtUtc;
                var head = await ReadHeadAsync(s.SourcePath, ct).ConfigureAwait(false);
                if (head is not null)
                {
                    messages = info.Length <= TranscriptHeadBytes
                        ? head.Count(b => b == (byte)'\n')
                        : null;
                    var derived = DeriveTitle(head);
                    if (derived is not null)
                    {
                        title = derived;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "session enrich skipped for {Path}", s.SourcePath);
        }

        var (repoName, branch) = await ResolveGitAsync(s.WorkingDirectory, ct).ConfigureAwait(false);

        return new SessionIndexEntryDto(
            s.Cli, s.SessionId, title,
            s.WorkingDirectory, repoName, branch,
            created, s.ModifiedAtUtc, messages, size,
            s.SourcePath, s.ResumeCommand,
            NativeChatClis.Contains(s.Cli), Live: false);
    }

    /// <summary>RF-006: first non-empty line of the first user message, ≤80c.</summary>
    internal static string? DeriveTitle(byte[] head)
    {
        try
        {
            var text = Encoding.UTF8.GetString(head);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || !trimmed.StartsWith('{'))
                {
                    continue;
                }

                var title = TryParseTitleLine(trimmed);
                if (title is not null)
                {
                    return title;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }

        return null;
    }

    /// <summary>Parses one transcript line and derives the title, or null.</summary>
    private static string? TryParseTitleLine(string trimmed)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            // Pretty-printed .json stores (opencode/gemini) yield
            // fragments per line — skip unparseable lines.
            return null;
        }

        using (doc)
        {
            var candidate = ExtractUserText(doc.RootElement);
            if (candidate is null)
            {
                return null;
            }

            // RF-006: the title is the first non-empty *line* of the
            // first user message — everything after \n is dropped.
            var firstLine = candidate.Split('\n')[0];
            var clean = MarkdownNoise.Replace(AnsiPattern.Replace(firstLine, " "), "");
            clean = WhitespaceRun.Replace(clean, " ").Trim();
            if (clean.Length == 0)
            {
                return null;
            }

            return clean.Length > 80 ? clean[..80] : clean;
        }
    }

    /// <summary>Pulls the first user-authored text out of a transcript line.</summary>
    private static string? ExtractUserText(JsonElement root)
    {
        // claude jsonl: {"type":"user","message":{"content":[{"text":...}]|"..."}}
        // codex rollout: {"type":"user_message","message":"..."} or payload.user_message
        // gemini session: {"messages":[{"type":"user","content":"..."}]}
        if (TryGetString(root, "message", out var m) && m.ValueKind == JsonValueKind.String)
        {
            return m.GetString();
        }

        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
        {
            if (msg.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }

                if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var t)
                            && t.ValueKind == JsonValueKind.String)
                        {
                            return t.GetString();
                        }
                    }
                }
            }
        }

        if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in msgs.EnumerateArray())
            {
                if (entry.TryGetProperty("content", out var c)
                    && c.ValueKind == JsonValueKind.String)
                {
                    return c.GetString();
                }
            }
        }

        return null;
    }

    private static bool TryGetString(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string ShortId(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];

    private static async Task<byte[]?> ReadHeadAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, useAsync: true);
        var take = (int)Math.Min(fs.Length, TranscriptHeadBytes);
        if (take == 0)
        {
            return null;
        }

        var buffer = new byte[take];
        var read = await fs.ReadAsync(buffer, ct).ConfigureAwait(false);
        return read == take ? buffer : buffer[..read];
    }

    private async Task<(string? RepoName, string? Branch)> ResolveGitAsync(
        string? cwd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            return (null, null);
        }

        if (_gitCache.TryGetValue(cwd, out var hit)
            && _time.GetUtcNow() - hit.At < GitCacheTtl)
        {
            return (hit.RepoName, hit.Branch);
        }

        var probe = await _gitProbe.ProbeAsync(cwd, ct).ConfigureAwait(false);
        _gitCache[cwd] = (probe.RepoName, probe.Branch, _time.GetUtcNow());
        return probe;
    }

    // ---- group / sort ----

    internal static IEnumerable<(string Key, string Label, List<SessionIndexEntryDto> Rows)> GroupRows(
        List<SessionIndexEntryDto> rows, string group) =>
        group switch
        {
            "project" => rows
                .GroupBy(r => r.RepoName ?? r.Cwd ?? "unknown", StringComparer.Ordinal)
                .Select(g => (g.Key, g.Key, g.ToList())),
            "day" => rows
                .GroupBy(r => r.ModifiedAtUtc.ToString("yyyy-MM-dd"), StringComparer.Ordinal)
                .Select(g => (g.Key, g.Key, g.ToList())),
            _ => rows
                .GroupBy(r => r.Cli, StringComparer.Ordinal)
                .Select(g => (g.Key, g.Key, g.ToList())),
        };

    internal static IReadOnlyList<SessionIndexEntryDto> SortRows(
        List<SessionIndexEntryDto> rows, string sort) =>
        sort switch
        {
            "created" => rows
                .OrderByDescending(r => r.Live)
                .ThenByDescending(r => r.CreatedAtUtc).ToList(),
            "title" => rows
                .OrderByDescending(r => r.Live)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            "duration" => rows
                .OrderByDescending(r => r.Live)
                .ThenByDescending(r => r.ModifiedAtUtc - r.CreatedAtUtc).ToList(),
            _ => rows
                .OrderByDescending(r => r.Live)
                .ThenByDescending(r => r.ModifiedAtUtc).ToList(),
        };
}
