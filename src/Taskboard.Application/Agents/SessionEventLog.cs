using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261022-session-event-log P1: JSONL-per-session event log under
/// <c>{dataDir}/sessions/{sessionId}/events.jsonl</c>. Append-only; readers
/// migrate supported historical generations forward and refuse unknown
/// future ones (RF-001/RF-002).
/// </summary>
public sealed class SessionEventLog : ISessionEventLog
{
    public const int CurrentGeneration = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootDir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, long> _nextSeq = new();

    public SessionEventLog(string dataDir)
    {
        _rootDir = Path.Combine(dataDir, "sessions");
    }

    public async Task<SessionEvent> AppendAsync(
        string sessionId, SessionEventKind kind, JsonElement payload, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var seq = _nextSeq.TryGetValue(path, out var cached)
                ? cached
                : File.Exists(path) ? CountLines(path) : 0;
            _nextSeq[path] = seq + 1;
            var evt = new SessionEvent(
                CurrentGeneration, seq, DateTimeOffset.UtcNow, kind, payload);
            var line = JsonSerializer.Serialize(evt, SerializerOptions);
            await File.AppendAllTextAsync(path, line + "\n", Encoding.UTF8, ct)
                .ConfigureAwait(false);
            return evt;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SessionEventReadResult> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
        {
            return new SessionEventReadResult(sessionId, Array.Empty<SessionEvent>(), false);
        }

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        var events = new List<SessionEvent>(lines.Length);
        var truncated = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SessionEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<SessionEvent>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                evt = null;
            }

            if (evt is null)
            {
                if (i == lines.Length - 1)
                {
                    truncated = true;
                    continue;
                }

                throw new JsonException($"Corrupt session event at line {i + 1} of '{path}'.");
            }

            events.Add(Migrate(evt, path, i + 1));
        }

        return new SessionEventReadResult(sessionId, events, truncated);
    }

    /// <summary>Adjacent vN→vN+1 migration chain (RF-002). v0→v1 is the
    /// identity step — gen-0 events predate the explicit generation tag.</summary>
    private static SessionEvent Migrate(SessionEvent evt, string path, int line)
    {
        var gen = evt.Generation;
        while (gen < CurrentGeneration)
        {
            evt = gen switch
            {
                0 => evt with { Generation = 1 },
                _ => throw new JsonException(
                    $"No session event migration from generation {gen} at line {line} of '{path}'.")
            };
            gen = evt.Generation;
        }

        return gen == CurrentGeneration
            ? evt
            : throw new UnknownSessionEventGenerationException(gen, CurrentGeneration);
    }

    private string PathFor(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || sessionId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid session id '{sessionId}'.", nameof(sessionId));
        }

        return Path.Combine(_rootDir, sessionId, "events.jsonl");
    }

    private static long CountLines(string path)
    {
        long n = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                n++;
            }
        }

        return n;
    }
}
