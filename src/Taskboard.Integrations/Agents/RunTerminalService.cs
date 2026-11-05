using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Integrations.Terminal;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261104-run-terminal-runtime: control plane over delegated run
/// terminals — persistent PTYs (runtime-owned sessions of
/// <see cref="TerminalSessionManager"/>) keyed <c>run-{taskId}</c> with
/// attention key <c>run:{taskId}</c>. Steering (send/interrupt), observation
/// (read/wait) and human attach bookkeeping live here; completion stays with
/// the delegation dispatcher.
/// </summary>
public sealed class RunTerminalService : IRunTerminalService
{
    /// <summary>Attention/session-key prefix of run terminals.</summary>
    internal const string AttentionKeyPrefix = "run:";

    /// <summary>PTY session id prefix — deterministic per task.</summary>
    internal const string SessionPrefix = "run-";

    /// <summary>Runtime user-key owning run-terminal PTYs.</summary>
    internal const string RuntimeUserKey = "runtime";

    internal const int ScreenLines = 30;
    internal const int HistoryCapChars = 64 * 1024;
    internal const int MaxConcurrentWaits = 8;
    internal static readonly TimeSpan MaxWaitTimeout = TimeSpan.FromSeconds(600);
    internal static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(180);
    internal static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan SendWaitAdvance = TimeSpan.FromSeconds(10);

    /// <summary>Interrupt sequence per CLI (RF-006 — manifests take over later).</summary>
    private static string InterruptSequence(string cliName) => cliName switch
    {
        "claude" or "codex" or "opencode" or "agy" or "antigravity" or "qwen" => "\x1b",
        _ => "\x03",
    };

    private sealed class WaitRegistration
    {
        public required string SessionId { get; init; }
        public Regex? OutputPattern { get; init; }
        public string? WantedState { get; init; }
        public string? Scope { get; init; }
        public TaskCompletionSource<RunWaitResult> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HashSet<string> SeenMessageIds { get; } = new(StringComparer.Ordinal);
    }

    private readonly TerminalSessionManager _terminals;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAttentionService? _attention;
    private readonly IAgentHookTokenService? _hookTokens;
    private readonly ISecretRedactor? _redactor;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<RunTerminalService> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, List<WaitRegistration>> _waits = new(StringComparer.Ordinal);

    public RunTerminalService(
        TerminalSessionManager terminals,
        IServiceScopeFactory scopeFactory,
        ILogger<RunTerminalService> logger,
        IAgentAttentionService? attention = null,
        IAgentHookTokenService? hookTokens = null,
        ISecretRedactor? redactor = null,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null)
    {
        _terminals = terminals;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _attention = attention;
        _hookTokens = hookTokens;
        _redactor = redactor;
        _configuration = configuration;
        _time = timeProvider ?? TimeProvider.System;
    }

    internal static string SessionIdFor(string taskId) =>
        // Same charset TerminalSessionManager normalizes to (letters, digits, -_:)
        // — task ids stay readable and the lookup key matches the stored key.
        $"{SessionPrefix}{string.Concat(taskId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ':' ? c : '-'))}";
    internal static string AttentionKeyFor(string taskId) => $"{AttentionKeyPrefix}{taskId}";

    /// <inheritdoc />
    public async Task<RunTerminalDto> EnsureAsync(EnsureRunTerminalRequest request, CancellationToken ct = default)
    {
        // One live terminal per dispatch — idempotent for retries/reattach.
        // A "live" row whose PTY is gone (server restart loses in-memory
        // sessions) is finished here so a fresh spawn isn't blocked by it.
        if (request.DispatchId is { } dispatchId)
        {
            var existing = await RepoCall(r => r.GetByDispatchAsync(dispatchId, ct)).ConfigureAwait(false);
            if (existing is { State: RunTerminalState.Live } liveByDispatch
                && _terminals.IsAlive(liveByDispatch.PtySessionId))
            {
                return liveByDispatch;
            }
        }

        var liveByTask = await RepoCall(r => r.GetLiveByTaskAsync(request.TaskId, ct)).ConfigureAwait(false);
        if (liveByTask is not null)
        {
            if (_terminals.IsAlive(liveByTask.PtySessionId))
            {
                return liveByTask;
            }

            await RepoCall(r => r.FinishAsync(
                liveByTask.Id, exitCode: null, archivedOutput: null,
                _time.GetUtcNow().UtcDateTime, ct)).ConfigureAwait(false);
        }

        var sessionId = SessionIdFor(request.TaskId);
        var attentionKey = AttentionKeyFor(request.TaskId);

        // Managed env: the session knows its own address + hook credentials
        // (the worker's callback channel — POST /api/agent-hooks + mailbox).
        var env = new Dictionary<string, string>(request.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["HARNESS_SESSION_ID"] = attentionKey,
        };
        var hookEnv = AgentHookEnv.Build(_hookTokens, _configuration, attentionKey);
        if (hookEnv is not null)
        {
            foreach (var (key, value) in hookEnv)
            {
                env[key] = value;
            }
        }

        var spawned = await _terminals.OpenDetachedAsync(
            RuntimeUserKey,
            sessionId,
            request.WorkingDirectory,
            request.Command,
            env,
            runtimeOwned: true,
            onChunk: OnChunk,
            onExited: (sid, code, tail) => OnSessionExitedAsync(sid, code, tail, CancellationToken.None),
            onHumanInput: sid => OnHumanInputAsync(sid, CancellationToken.None),
            onViewersEmpty: sid => OnViewersEmptyAsync(sid, CancellationToken.None),
            attentionKey: attentionKey).ConfigureAwait(false);

        if (!spawned && !_terminals.IsAlive(sessionId))
        {
            throw new InvalidOperationException($"run terminal '{sessionId}' failed to spawn");
        }

        var dto = await RepoCall(r => r.AddAsync(
            request with { Environment = null },
            $"rt-{Guid.NewGuid():N}", sessionId, ct))
            .ConfigureAwait(false);
        return dto;
    }

    /// <inheritdoc />
    public async Task<RunTerminalDto?> GetAsync(string target, string? scope = null, CancellationToken ct = default)
    {
        var dto = await ResolveAsync(target, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return null;
        }

        if (scope is not null)
        {
            var taskScope = await TaskScopeAsync(dto.TaskId, ct).ConfigureAwait(false);
            if (!string.Equals(taskScope, scope, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return await EnrichAsync(dto, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RunTerminalOpResult> SendAsync(
        string target, string text, bool wait = false, bool force = false,
        string? scope = null, CancellationToken ct = default)
    {
        var dto = await GetAsync(target, scope, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return new(false, null, new(RunTerminalErrorKinds.NotFound, $"no run terminal for '{target}'"));
        }

        if (dto.State == RunTerminalState.Finished)
        {
            return new(false, dto, new(RunTerminalErrorKinds.Finished,
                $"run terminal {dto.Id} is finished", "run_read still returns archived output"));
        }

        if (dto.ControlOwner == RunTerminalControlOwner.Human)
        {
            return new(false, dto, new(RunTerminalErrorKinds.StatusMismatch,
                "a person is driving this terminal", "detach the viewer or wait for control to return"));
        }

        var status = await StatusOfAsync(dto.TaskId, ct).ConfigureAwait(false);
        if (!force && status is "blocked" or "error" or "waiting_you")
        {
            return new(false, dto, new(RunTerminalErrorKinds.Blocked,
                $"agent is {status} — send is refused",
                "run_read the screen, resolve the prompt, or retry with force: true"));
        }

        var payload = text.EndsWith('\n') ? text : text + "\r";
        if (!await _terminals.InjectAsync(dto.PtySessionId, payload).ConfigureAwait(false))
        {
            return new(false, dto, new(RunTerminalErrorKinds.Finished,
                "terminal is not accepting input", "run_read the archived output"));
        }

        var advanced = wait
            ? await WaitForAdvanceAsync(dto, SendWaitAdvance, ct).ConfigureAwait(false)
            : (bool?)null;

        var updated = await GetAsync(dto.Id, scope, ct).ConfigureAwait(false) ?? dto;
        return new(true, updated, null, advanced);
    }

    /// <inheritdoc />
    public async Task<RunReadResult> ReadAsync(
        string target, int? lines = null, bool history = false,
        string? scope = null, CancellationToken ct = default)
    {
        var dto = await GetAsync(target, scope, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return new(target, string.Empty, false, null, null,
                new(RunTerminalErrorKinds.NotFound, $"no run terminal for '{target}'"));
        }

        // Read-after-exit: archived output (RF-001 retained-for-read).
        if (dto.State == RunTerminalState.Finished)
        {
            return new(target, Redact(Tail(dto.ArchivedOutput, lines, HistoryCapChars)),
                false, null, dto);
        }

        var status = await StatusOfAsync(dto.TaskId, ct).ConfigureAwait(false);
        var scrollback = _terminals.GetScrollback(dto.PtySessionId);
        var busy = status is "working" or "blocked" or "error" or "waiting_you";

        if (history && !busy)
        {
            return new(target, Redact(Tail(scrollback, lines, HistoryCapChars)), false, null, dto);
        }

        if (history && busy)
        {
            // RF-004: history reads never inject scroll input into a live
            // agent — screen snapshot + incomplete marker instead.
            return new(target, Redact(TailLines(scrollback, ScreenLines)),
                true, $"agent is {status} — history unavailable", dto);
        }

        return new(target, Redact(TailLines(scrollback, lines ?? ScreenLines)), false, null, dto);
    }

    /// <inheritdoc />
    public async Task<RunWaitResult> WaitAsync(RunWaitRequest request, string? scope = null, CancellationToken ct = default)
    {
        if (request.State is null && request.Output is null)
        {
            return new(RunWaitReason.Timeout, null, null, null,
                new(RunTerminalErrorKinds.NeedsTarget, "pass state and/or output to wait on"));
        }

        var dto = await GetAsync(request.Target, scope, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return new(RunWaitReason.Timeout, null, null, null,
                new(RunTerminalErrorKinds.NotFound, $"no run terminal for '{request.Target}'"));
        }

        if (dto.State == RunTerminalState.Finished)
        {
            return new(RunWaitReason.Exited, null, null, dto);
        }

        Regex? pattern = null;
        if (request.Output is { } output)
        {
            try
            {
                pattern = new Regex(output, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException ex)
            {
                return new(RunWaitReason.Timeout, null, null, dto,
                    new(RunTerminalErrorKinds.NeedsTarget, $"invalid output regex: {ex.Message}"));
            }
        }

        var registrations = _waits.GetOrAdd(dto.PtySessionId, _ => []);
        lock (registrations)
        {
            if (registrations.Count >= MaxConcurrentWaits)
            {
                return new(RunWaitReason.Timeout, null, null, dto,
                    new(RunTerminalErrorKinds.Blocked, $"too many concurrent waits on {dto.Id}"));
            }
        }

        var taskScope = scope ?? await TaskScopeAsync(dto.TaskId, ct).ConfigureAwait(false);
        var registration = new WaitRegistration
        {
            SessionId = dto.PtySessionId,
            OutputPattern = pattern,
            WantedState = NormalizeState(request.State),
            Scope = taskScope,
        };
        if (taskScope is not null)
        {
            foreach (var m in await MailboxSnapshotAsync(taskScope, ct).ConfigureAwait(false))
            {
                registration.SeenMessageIds.Add(m);
            }
        }

        lock (registrations)
        {
            registrations.Add(registration);
        }

        var timeout = request.Timeout is { } t
            ? (t > MaxWaitTimeout ? MaxWaitTimeout : t)
            : DefaultWaitTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (registration.Tcs.Task.IsCompleted)
                {
                    return await registration.Tcs.Task.ConfigureAwait(false);
                }

                // Exited?
                if (!_terminals.IsAlive(dto.PtySessionId))
                {
                    return new(RunWaitReason.Exited, null, null,
                        await GetAsync(dto.Id, scope, CancellationToken.None).ConfigureAwait(false));
                }

                // State match?
                if (registration.WantedState is { } wanted)
                {
                    var status = await StatusOfAsync(dto.TaskId, token).ConfigureAwait(false);
                    if (string.Equals(status, wanted, StringComparison.Ordinal))
                    {
                        return new(RunWaitReason.State, status, null, dto);
                    }
                }

                // Mailbox delivery?
                if (taskScope is not null)
                {
                    var fresh = await MailboxSnapshotAsync(taskScope, token).ConfigureAwait(false);
                    var arrived = fresh.FirstOrDefault(id => !registration.SeenMessageIds.Contains(id));
                    if (arrived is not null)
                    {
                        return new(RunWaitReason.Message, null, arrived, dto);
                    }
                }

                await Task.Delay(WaitPollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout = check-in, not failure (RF-005).
            return new(RunWaitReason.Timeout, null, null,
                await GetAsync(dto.Id, scope, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            lock (registrations)
            {
                registrations.Remove(registration);
            }
        }

        return new(RunWaitReason.Timeout, null, null, dto);
    }

    /// <inheritdoc />
    public async Task<RunTerminalOpResult> InterruptAsync(string target, string? scope = null, CancellationToken ct = default)
    {
        var dto = await GetAsync(target, scope, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return new(false, null, new(RunTerminalErrorKinds.NotFound, $"no run terminal for '{target}'"));
        }

        if (dto.State == RunTerminalState.Finished)
        {
            return new(false, dto, new(RunTerminalErrorKinds.Finished, $"run terminal {dto.Id} is finished"));
        }

        if (!await _terminals.InjectAsync(dto.PtySessionId, InterruptSequence(dto.CliName)).ConfigureAwait(false))
        {
            return new(false, dto, new(RunTerminalErrorKinds.Finished, "terminal is not accepting input"));
        }

        return new(true, dto);
    }

    /// <inheritdoc />
    public async Task RecordFirstMessageAsync(string dispatchId, bool sent, string? reason, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatches = scope.ServiceProvider.GetRequiredService<IDispatchRepository>();
        await dispatches.RecordFirstMessageAsync(
            dispatchId, sent, reason, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnSessionExitedAsync(
        string sessionId, int exitCode, string scrollbackTail, CancellationToken ct = default)
    {
        var dto = await RepoCall(r => r.GetByPtySessionIdAsync(sessionId, ct)).ConfigureAwait(false);
        if (dto is null || dto.State == RunTerminalState.Finished)
        {
            return;
        }

        var tail = Tail(scrollbackTail, null, HistoryCapChars);
        var finished = await RepoCall(r => r.FinishAsync(
            dto.Id, exitCode, tail, _time.GetUtcNow().UtcDateTime, ct)).ConfigureAwait(false);
        _hookTokens?.Revoke(AttentionKeyFor(dto.TaskId));
        ResolveWaits(sessionId, new RunWaitResult(RunWaitReason.Exited, null, null, finished ?? dto));
        _logger.LogInformation(
            "Run terminal {Id} finished (session {SessionId}, exit {ExitCode}).",
            dto.Id, sessionId, exitCode);
    }

    /// <inheritdoc />
    public async Task OnHumanInputAsync(string sessionId, CancellationToken ct = default)
    {
        var dto = await RepoCall(r => r.GetByPtySessionIdAsync(sessionId, ct)).ConfigureAwait(false);
        if (dto is null || dto.State != RunTerminalState.Live
            || dto.ControlOwner == RunTerminalControlOwner.Human)
        {
            return;
        }

        await RepoCall(r => r.SetControlOwnerAsync(dto.Id, RunTerminalControlOwner.Human, ct))
            .ConfigureAwait(false);
        await EmitFeedAsync(dto, "human took control", $"person typing on {sessionId}", ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnViewersEmptyAsync(string sessionId, CancellationToken ct = default)
    {
        var dto = await RepoCall(r => r.GetByPtySessionIdAsync(sessionId, ct)).ConfigureAwait(false);
        if (dto is null || dto.ControlOwner != RunTerminalControlOwner.Human)
        {
            return;
        }

        await RepoCall(r => r.SetControlOwnerAsync(dto.Id, RunTerminalControlOwner.Operator, ct))
            .ConfigureAwait(false);
        await EmitFeedAsync(dto, "operator regained control", $"viewers detached from {sessionId}", ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> AttachViewerAsync(
        string target, string connectionId, string userKey,
        Func<string, string, Task> onOutput, Func<string, string, Task> onClosed,
        CancellationToken ct = default)
    {
        var dto = await ResolveAsync(target, ct).ConfigureAwait(false);
        if (dto is null || dto.State != RunTerminalState.Live)
        {
            return null;
        }

        var attached = _terminals.AttachViewer(userKey, connectionId, dto.PtySessionId, onOutput, onClosed);
        if (attached)
        {
            await EmitFeedAsync(dto, "terminal attached", $"viewer on {dto.PtySessionId}", ct)
                .ConfigureAwait(false);
        }

        return attached ? dto.PtySessionId : null;
    }

    /// <inheritdoc />
    public Task DetachViewerAsync(string sessionId, string connectionId, CancellationToken ct = default)
    {
        _terminals.DetachViewer(sessionId, connectionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnpinAsync(string sessionId, CancellationToken ct = default)
    {
        _terminals.Unpin(sessionId);
        return Task.CompletedTask;
    }

    // ---- internals ----

    /// <summary>Output tap from the PTY — output waits match post-registration bytes only (RF-005).</summary>
    private Task OnChunk(string sessionId, string chunk)
    {
        if (!_waits.TryGetValue(sessionId, out var registrations))
        {
            return Task.CompletedTask;
        }

        WaitRegistration[] snapshot;
        lock (registrations)
        {
            snapshot = [.. registrations];
        }

        foreach (var wait in snapshot)
        {
            if (wait.OutputPattern?.IsMatch(chunk) == true)
            {
                wait.Tcs.TrySetResult(new RunWaitResult(RunWaitReason.Output, null, chunk.Trim(), null));
            }
        }

        return Task.CompletedTask;
    }

    private void ResolveWaits(string sessionId, RunWaitResult result)
    {
        if (!_waits.TryGetValue(sessionId, out var registrations))
        {
            return;
        }

        lock (registrations)
        {
            foreach (var wait in registrations)
            {
                wait.Tcs.TrySetResult(result);
            }
        }
    }

    /// <summary>run_send --wait: the turn advanced when output flows or state moves within the window.</summary>
    private async Task<bool> WaitForAdvanceAsync(RunTerminalDto dto, TimeSpan window, CancellationToken ct)
    {
        var before = await StatusOfAsync(dto.TaskId, ct).ConfigureAwait(false);
        var deadline = _time.GetUtcNow() + window;
        while (_time.GetUtcNow() < deadline && !ct.IsCancellationRequested)
        {
            var status = await StatusOfAsync(dto.TaskId, ct).ConfigureAwait(false);
            if (!string.Equals(status, before, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<RunTerminalDto?> ResolveAsync(string target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var t = target.Trim();
        if (t.StartsWith("rt-", StringComparison.Ordinal))
        {
            return await RepoCall(r => r.GetAsync(t, ct)).ConfigureAwait(false);
        }

        if (t.StartsWith("disp-", StringComparison.Ordinal))
        {
            return await RepoCall(r => r.GetByDispatchAsync(t, ct)).ConfigureAwait(false);
        }

        if (t.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            return await RepoCall(r => r.GetByPtySessionIdAsync(t, ct)).ConfigureAwait(false);
        }

        return await RepoCall(r => r.GetLiveByTaskAsync(t, ct)).ConfigureAwait(false)
            ?? await RepoCall(r => r.GetByPtySessionIdAsync(t, ct)).ConfigureAwait(false);
    }

    /// <summary>Live facts the row doesn't carry: attention status + dispatch first-message.</summary>
    private async Task<RunTerminalDto> EnrichAsync(RunTerminalDto dto, CancellationToken ct)
    {
        var status = await StatusOfAsync(dto.TaskId, ct).ConfigureAwait(false);
        if (dto.DispatchId is null)
        {
            return dto with { Status = status };
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatches = scope.ServiceProvider.GetRequiredService<IDispatchRepository>();
        var dispatch = await dispatches.GetAsync(dto.DispatchId, ct).ConfigureAwait(false);
        return dto with
        {
            Status = status,
            FirstMessageSent = dispatch?.FirstMessageSentAtUtc is not null,
            FirstMessageReason = dispatch?.FirstMessageFailedReason,
        };
    }

    private async Task<string?> StatusOfAsync(string taskId, CancellationToken ct)
    {
        if (_attention is null)
        {
            return null;
        }

        try
        {
            var snapshot = await _attention.SnapshotAsync([AttentionKeyFor(taskId)], ct).ConfigureAwait(false);
            return NormalizeState(snapshot.FirstOrDefault()?.State.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "attention snapshot failed for run:{TaskId}", taskId);
            return null;
        }
    }

    private async Task<string?> TaskScopeAsync(string taskId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IDelegationTaskRepository>();
        return (await tasks.GetAsync(taskId, ct).ConfigureAwait(false))?.Scope;
    }

    private async Task<IReadOnlyList<string>> MailboxSnapshotAsync(string scope, CancellationToken ct)
    {
        await using var di = _scopeFactory.CreateAsyncScope();
        var mailbox = di.ServiceProvider.GetRequiredService<IAgentMailboxRepository>();
        var messages = await mailbox.ListByScopeAsync(scope, take: 50, ct).ConfigureAwait(false);
        return messages.Select(m => m.Id).ToList();
    }

    private async Task<T> RepoCall<T>(Func<IRunTerminalRepository, Task<T>> call)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRunTerminalRepository>();
        return await call(repo).ConfigureAwait(false);
    }

    private async Task EmitFeedAsync(RunTerminalDto dto, string title, string body, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var feed = scope.ServiceProvider.GetService<IAgentFeedService>();
            if (feed is null)
            {
                return;
            }

            await feed.ProduceAsync(
                new FeedItemRequest(
                    AgentFeedKind.Attention,
                    $"run terminal {title}",
                    body,
                    DeepLink: $"run-terminal:{dto.Id}",
                    SessionKey: AttentionKeyFor(dto.TaskId),
                    Cli: dto.CliName,
                    IssueNumber: null,
                    NeedsAttention: false,
                    UserId: "local"),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "feed emit failed for {Id}", dto.Id);
        }
    }

    private string Redact(string text) => _redactor?.Redact(text) ?? text;

    private static string? NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state)
            ? null
            : state.Trim().ToLowerInvariant() switch
            {
                "waitingyou" or "waiting-you" or "waiting_you" => "waiting_you",
                var s => s,
            };

    private static string Tail(string? text, int? lines, int capChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var slice = text.Length > capChars ? text[^capChars..] : text;
        return lines is { } n ? TailLines(slice, n) : slice;
    }

    private static string TailLines(string? text, int lines)
    {
        if (string.IsNullOrEmpty(text) || lines <= 0)
        {
            return string.Empty;
        }

        var all = text.Split('\n');
        return all.Length <= lines ? text : string.Join('\n', all[^lines..]);
    }
}
