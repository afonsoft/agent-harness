using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-002/RF-004: executes the
/// <c>setup:</c> command list sequentially inside the new worktree via
/// <c>bash -lc</c>. Output is secret-scrubbed and tailed (≤64KB on the card,
/// ≤4KB for the preamble). A failing command stops the list and marks the
/// card <c>failed</c> — the worktree is kept. Card writes go through a fresh
/// DI scope so this singleton can also drive background runs
/// (<c>start-immediately</c>).
/// </summary>
public sealed class WorktreeSetupRunner : IWorktreeSetupRunner
{
    private const string TimeoutKey = "Taskboard:Worktree:SetupTimeoutSeconds";
    private const int DefaultTimeoutSeconds = 600;
    private const int MinTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 3600;
    private const int MaxLogBytes = 64 * 1024;
    private const int PreambleTailBytes = 4 * 1024;

    private readonly IProcessRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorktreeSetupRunner> _logger;

    public WorktreeSetupRunner(
        IProcessRunner runner,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<WorktreeSetupRunner> logger)
    {
        _runner = runner;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorktreeSetupResult> RunSetupAsync(
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        await SetSetupAsync(
                worktreePath, repositoryPath, WorktreeSetupStates.Running,
                exitCode: null, logTail: null, cancellationToken)
            .ConfigureAwait(false);

        var log = new StringBuilder();
        var timeout = PerCommandTimeout();
        int? exitCode = null;
        var state = WorktreeSetupStates.Ok;

        foreach (var command in commands)
        {
            log.Append("$ ").AppendLine(command);
            ProcessRunResult result;
            try
            {
                result = await _runner.RunAsync(
                        "bash", worktreePath, ["-lc", command], timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Setup command failed to start: {Cmd}", command);
                log.AppendLine(SecretScrubber.Scrub(ex.Message));
                state = WorktreeSetupStates.Failed;
                exitCode = -1;
                break;
            }

            AppendScrubbed(log, result.StdOut);
            AppendScrubbed(log, result.StdErr);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                log.AppendLine(result.TimedOut
                    ? $"# timed out after {timeout.TotalSeconds:0}s"
                    : $"# exit code {result.ExitCode}");
                state = WorktreeSetupStates.Failed;
                exitCode = result.TimedOut ? -1 : result.ExitCode;
                break;
            }
        }

        var tail = Tail(log.ToString());
        var finishedAt = DateTime.UtcNow;
        await SetSetupAsync(
                worktreePath, repositoryPath, state, exitCode, tail, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Worktree setup {State} in {Path} ({Count} command(s), exit {Exit}).",
            state, worktreePath, commands.Count, exitCode);
        return new WorktreeSetupResult(state, exitCode, tail, startedAt, finishedAt);
    }

    /// <inheritdoc />
    public void RunSetupInBackground(
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string> commands)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RunSetupAsync(repositoryPath, worktreePath, commands)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Background path must never bubble into the dispatcher —
                // the card already records failures; this only guards the
                // service-level crash before the card write.
                _logger.LogWarning(ex, "Background worktree setup crashed in {Path}.", worktreePath);
                await SetSetupAsync(
                        worktreePath, repositoryPath, WorktreeSetupStates.Failed,
                        exitCode: -1, logTail: $"setup crashed: {ex.GetType().Name}",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        });
    }

    /// <summary>Card write via a fresh scope — the runner is a singleton and
    /// may outlive the request scope that triggered it.</summary>
    private async Task SetSetupAsync(
        string worktreePath, string repoPath, string state,
        int? exitCode, string? logTail, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cards = scope.ServiceProvider.GetRequiredService<IWorktreeCardService>();
            await cards.SetSetupAsync(worktreePath, repoPath, state, exitCode, logTail, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record setup state {State} on {Path}.", state, worktreePath);
        }
    }

    private TimeSpan PerCommandTimeout()
    {
        var raw = _configuration.GetValue<int?>(TimeoutKey) ?? DefaultTimeoutSeconds;
        var clamped = Math.Clamp(raw, MinTimeoutSeconds, MaxTimeoutSeconds);
        return TimeSpan.FromSeconds(clamped);
    }

    private static void AppendScrubbed(StringBuilder log, string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        log.Append(SecretScrubber.Scrub(output));
        if (!output.EndsWith('\n'))
        {
            log.AppendLine();
        }
    }

    private static string Tail(string log) =>
        log.Length <= MaxLogBytes ? log : log[^MaxLogBytes..];

    /// <summary>Tail sized for the worker preamble (RF-004).</summary>
    public static string PreambleTail(string? log) =>
        log is null || log.Length <= PreambleTailBytes ? log ?? string.Empty : log[^PreambleTailBytes..];
}
