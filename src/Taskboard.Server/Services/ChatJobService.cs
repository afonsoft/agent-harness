using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Repositories;
using Taskboard.Application.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Integrations.Execution;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// Live-process registry for chat background jobs (SPEC-20261005-chat-jobs-schedule-search
/// RF-001/RF-002 + the boot-sweep RF): <c>shell_exec run_in_background</c>
/// spawns a detached process whose stdout/stderr stream to a per-job file
/// under the data dir, the durable <see cref="ChatJob"/> row tracks lifecycle,
/// and completion posts a system note to the transcript so the next turn sees
/// the outcome. Singleton — the live handles span requests/scopes; every EF
/// touch goes through a fresh scope.
/// </summary>
public sealed class ChatJobService : IChatJobService, IDisposable
{
    /// <summary>Output tail served by <c>job_output</c>/the endpoint — matches the chat cap.</summary>
    public const int MaxOutputTailBytes = 64 * 1024;
    private const int DefaultTailBytes = 16 * 1024;

    private sealed record JobHandle(
        Process Process, Task Runner, CancellationTokenSource KillSwitch);

    private readonly ConcurrentDictionary<string, JobHandle> _live = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecretRedactor _redactor;
    private readonly ILogger<ChatJobService> _logger;
    private readonly string _jobsDir;
    private volatile bool _disposed;

    public ChatJobService(
        IServiceScopeFactory scopeFactory,
        ISecretRedactor redactor,
        ILogger<ChatJobService> logger,
        string dataDir)
    {
        _scopeFactory = scopeFactory;
        _redactor = redactor;
        _logger = logger;
        _jobsDir = Path.Combine(dataDir, "jobs");
        Directory.CreateDirectory(_jobsDir);
    }

    public async Task<ChatJobDto> StartAsync(
        string conversationId, string? runId, string command, string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var job = ChatJob.Create(
            ChatJobId.NewGuid(), ChatConversationId.From(conversationId),
            runId is null ? null : ChatRunId.From(runId), command);

        var outputPath = Path.Combine(_jobsDir, $"{job.Id.Value}.log");

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        WithoutHarnessEnv.Apply(startInfo.Environment, workspacePath);

        var process = Process.Start(startInfo)
            ?? throw new ChatValidationException("failed to spawn background job process");
        job.MarkRunning(process.Id, outputPath);

        var killSwitch = new CancellationTokenSource();
        // Gate: a fast-exit process must not settle before the handle is
        // registered AND the row exists — otherwise SettleAsync finds no
        // row, returns early, and the job stays 'running' forever (seen
        // on CI with an instant `echo` under load).
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = Task.Run(async () =>
        {
            await startGate.Task.ConfigureAwait(false);
            await RunJobAsync(job.Id, process, outputPath).ConfigureAwait(false);
        }, CancellationToken.None);
        _live[job.Id.Value] = new JobHandle(process, runner, killSwitch);

        try
        {
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
                await jobs.AddAsync(job, cancellationToken).ConfigureAwait(false);
                await jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            startGate.TrySetCanceled(cancellationToken);
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // Already exited — nothing to clean up.
            }

            throw;
        }

        startGate.TrySetResult();
        return ToDto(job);
    }

    public async Task<IReadOnlyList<ChatJobDto>> ListAsync(
        string conversationId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
        var convId = ChatConversationId.From(conversationId);
        var rows = await jobs.Query
            .Where(j => j.ConversationId == convId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .Where(j => !activeOnly || j.Status.IsActive)
            .Select(ToDto).ToList();
    }

    public async Task<ChatJobOutputDto?> GetOutputAsync(
        string conversationId, string jobId, int tailBytes,
        CancellationToken cancellationToken = default)
    {
        var job = await GetOwnedAsync(conversationId, jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return null;
        }

        var tail = string.Empty;
        if (job.OutputPath is { } path && File.Exists(path))
        {
            var bytes = tailBytes <= 0 ? DefaultTailBytes : Math.Min(tailBytes, MaxOutputTailBytes);
            tail = await ReadTailAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            tail = _redactor.Redact(tail) ?? string.Empty;
        }

        return new ChatJobOutputDto(
            job.Id.Value, job.Status.Value, job.ExitCode, tail);
    }

    public async Task<ChatJobDto?> KillAsync(
        string conversationId, string jobId, CancellationToken cancellationToken = default)
    {
        var job = await GetOwnedAsync(conversationId, jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return null;
        }

        if (job.Status.IsTerminal)
        {
            throw new ChatJobConflictException($"Job '{jobId}' is already {job.Status.Value}.");
        }

        if (_live.TryGetValue(job.Id.Value, out var handle))
        {
            // The runner settles the row (killed + exit code) — it owns the EF write.
            await handle.KillSwitch.CancelAsync().ConfigureAwait(false);
            try
            {
                handle.Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // Process already exited between checks — the runner reports it.
            }
        }
        else
        {
            // No live handle (row survived a restart) — mark terminated.
            // `job` veio de um scope já descartado (GetOwnedAsync): reattach
            // via UpdateAsync para o Terminate persistir no contexto novo.
            job.Terminate("no live process handle (host restarted)");
            await using var scope = _scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
            await jobs.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
            await jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(job);
    }

    /// <summary>
    /// Boot sweep: rows left <c>queued|running</c> by a dead host — the OS
    /// process is orphaned (PID reuse makes signaling it unsafe), so the row
    /// goes <c>terminated</c> and the transcript gets a marker note.
    /// </summary>
    public async Task TerminateOrphansAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
        var orphans = await jobs.Query
            .Where(j => j.Status == ChatJobStatus.Queued || j.Status == ChatJobStatus.Running)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (orphans.Count == 0)
        {
            return;
        }

        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        foreach (var job in orphans)
        {
            job.Terminate("host restarted — process orphaned");
            try
            {
                await chat.PostSystemNoteAsync(
                    job.ConversationId.Value,
                    $"Background job `{job.Id.Value[..8]}` terminated — server restarted while it was running. Command: `{job.Command}`",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "orphan-note failed for job {JobId}", job.Id.Value);
            }
        }

        await jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Shutdown: kill live children and mark their rows terminated.</summary>
    public async Task TerminateAllAsync()
    {
        foreach (var handle in _live.Values)
        {
            try
            {
                await handle.KillSwitch.CancelAsync().ConfigureAwait(false);
                handle.Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // Already exited.
            }
        }

        try
        {
            await Task.WhenAll(_live.Values.Select(h => h.Runner))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "job shutdown wait timed out — {Count} runner(s) abandoned", _live.Count);
        }
        catch (OperationCanceledException)
        {
            // A runner gated off by a failed StartAsync persist cancels —
            // nothing left to wait for.
        }
    }

    private async Task<ChatJob?> GetOwnedAsync(
        string conversationId, string jobId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
        var job = await jobs.GetAsync(ChatJobId.From(jobId), cancellationToken).ConfigureAwait(false);
        return job is not null
            && string.Equals(job.ConversationId.Value, conversationId, StringComparison.Ordinal)
                ? job
                : null;
    }

    /// <summary>
    /// Streams stdout+stderr to the job's log file until exit, then settles
    /// the row (killed vs finished by the kill switch) and posts the
    /// completion system note (RF-001).
    /// </summary>
    private async Task RunJobAsync(ChatJobId jobId, Process process, string outputPath)
    {
        var killed = false;
        try
        {
            await using (var stream = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                await Task.WhenAll(
                    PumpAsync(process.StandardOutput, stream),
                    PumpAsync(process.StandardError, stream)).ConfigureAwait(false);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "job {JobId} pump failed", jobId.Value);
        }

        if (_live.TryGetValue(jobId.Value, out var handle))
        {
            killed = handle.KillSwitch.IsCancellationRequested;
            _live.TryRemove(jobId.Value, out _);
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        await SettleAsync(jobId, killed, exitCode).ConfigureAwait(false);
        process.Dispose();
    }

    private static async Task PumpAsync(StreamReader reader, FileStream stream)
    {
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(buffer, 0, read))
                .ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
    }

    private async Task SettleAsync(ChatJobId jobId, bool killed, int exitCode)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<ChatJob>>();
            var job = await jobs.GetAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (job is null || job.Status.IsTerminal)
            {
                return;
            }

            if (killed)
            {
                job.Kill(exitCode);
            }
            else
            {
                job.Finish(exitCode);
            }

            await jobs.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
            var outcome = job.Status == ChatJobStatus.Killed ? "killed" : $"finished (exit {exitCode})";
            var tail = await TailSnippetAsync(job.OutputPath).ConfigureAwait(false);
            await chat.PostSystemNoteAsync(
                job.ConversationId.Value,
                $"Background job `{job.Id.Value[..8]}` {outcome}.\nCommand: `{job.Command}`\nTail:\n```\n{tail}\n```",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "job {JobId} settle failed", jobId.Value);
        }
    }

    private static async Task<string> TailSnippetAsync(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return "(no output)";
        }

        var tail = await ReadTailAsync(path, 2000, CancellationToken.None).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(tail) ? "(empty output)" : tail.TrimEnd();
    }

    private static async Task<string> ReadTailAsync(
        string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var offset = Math.Max(0, length - maxBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[length - offset];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, total);
        if (offset > 0)
        {
            // Skip the partial first line of a tail cut mid-line.
            var firstNewline = text.IndexOf('\n');
            text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
        }

        return text;
    }

    private static ChatJobDto ToDto(ChatJob job) => new(
        Id: job.Id.Value,
        ConversationId: job.ConversationId.Value,
        Command: job.Command,
        Status: job.Status.Value,
        ExitCode: job.ExitCode,
        Error: job.Error,
        CreatedAt: job.CreatedAt,
        StartedAt: job.StartedAt,
        FinishedAt: job.FinishedAt);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var handle in _live.Values)
        {
            try
            {
                handle.KillSwitch.Cancel();
                handle.Process.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // Already gone.
            }

            handle.KillSwitch.Dispose();
        }
    }
}
