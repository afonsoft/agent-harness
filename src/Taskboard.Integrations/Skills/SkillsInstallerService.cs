using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Integrations.Agents;
using Taskboard.Skills;

namespace Taskboard.Integrations.Skills;

/// <summary>
/// Installs the configured skills repository globally — <c>npx skills add
/// &lt;repo&gt; -g --all --copy</c> followed by the repo's
/// <c>install.sh --all</c> — and reports the installed state by scanning the
/// global skills directories (SPEC-20260917-skills-installer).
/// </summary>
public sealed class SkillsInstallerService : ISkillsInstallerService
{
    internal const string ManifestFileName = "skills-install.json";

    private static readonly string[] RequiredTools = ["npx", "git"];
    private static readonly string[] OptionalTools = ["bash"];
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(300);

    private sealed record LastRun(
        DateTimeOffset AtUtc,
        long DurationMs,
        string? Repository,
        IReadOnlyList<SkillsInstallStep> Steps,
        string? Error);

    private readonly IConfiguration _configuration;
    private readonly ILogger<SkillsInstallerService> _logger;
    private readonly string _cacheDirectory;
    private readonly string _homeDirectory;
    private readonly string _manifestPath;
    private readonly Func<CancellationToken, Task<string?>>? _accessTokenProvider;
    private readonly ISkillsInstallRunner _runner;
    private readonly Func<string, string?> _locator;
    private readonly SkillsOperationLog? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile SkillsSyncState _state = SkillsSyncState.Idle;
    private volatile LastRun? _lastRun;

    public SkillsInstallerService(
        IConfiguration configuration,
        ILogger<SkillsInstallerService> logger,
        string dataDirectory,
        string homeDirectory,
        Func<CancellationToken, Task<string?>>? accessTokenProvider = null,
        ISkillsInstallRunner? runner = null,
        Func<string, string?>? executableLocator = null,
        SkillsOperationLog? log = null)
    {
        _configuration = configuration;
        _logger = logger;
        _cacheDirectory = Path.Join(dataDirectory, "skills-cache");
        _homeDirectory = homeDirectory;
        _manifestPath = Path.Join(dataDirectory, ManifestFileName);
        _accessTokenProvider = accessTokenProvider;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _log = log;
    }

    public SkillsInstallStatus GetStatus()
    {
        var manifest = InstallManifest.Load(_manifestPath);
        var locations = ScanLocations();
        var total = locations.Sum(l => l.Count);
        var run = _lastRun;

        return new SkillsInstallStatus(
            _state,
            Installed: total > 0,
            SkillCount: total,
            LastRunUtc: run?.AtUtc ?? manifest?.InstalledAtUtc,
            LastDurationMs: run?.DurationMs ?? manifest?.DurationMs,
            Repository: run?.Repository ?? manifest?.Repository ?? SafeResolveRepository(),
            Prerequisites: DetectPrerequisites(),
            Steps: run?.Steps ?? [],
            Locations: locations,
            Error: run?.Error);
    }

    public void RequestInstall()
    {
        if (!_gate.Wait(0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await InstallCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public async Task<SkillsInstallStatus> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
            return await InstallCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<SkillsInstallStatus> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var locations = ScanLocations();
        var total = locations.Sum(l => l.Count);
        _log?.Info($"Skills verify: {total} skill(s) found in {locations.Count} location(s).");

        var manifest = InstallManifest.Load(_manifestPath) ?? new InstallManifest();
        manifest.SkillCount = total;
        manifest.Save(_manifestPath);

        return Task.FromResult(GetStatus());
    }

    private async Task<SkillsInstallStatus> InstallCoreAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        _state = SkillsSyncState.Running;
        _lastRun = new LastRun(started, 0, null, [], null);

        var repository = string.Empty;
        var steps = new List<SkillsInstallStep>();
        string? error = null;

        try
        {
            repository = SkillsRepository.Resolve(_configuration);
            _log?.Info($"Skills install started — repository '{repository}'.");

            var missing = RequiredTools.Where(tool => _locator(tool) is null).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"PrerequisiteMissing: {string.Join(", ", missing)}");
            }

            var npx = _locator("npx")!;
            var bash = _locator("bash");
            var token = _accessTokenProvider is null
                ? null
                : await _accessTokenProvider(cancellationToken).ConfigureAwait(false);

            steps.Add(await RunStepAsync(
                "npx-add",
                npx,
                _homeDirectory,
                ["skills", "add", repository, "-g", "--all", "--copy"],
                cancellationToken).ConfigureAwait(false));

            if (steps[^1].State == SkillsInstallStepState.Failed)
            {
                steps.Add(new SkillsInstallStep(
                    "install-sh", SkillsInstallStepState.Skipped, null, null, "previous step failed"));
            }
            else if (bash is null)
            {
                steps.Add(new SkillsInstallStep(
                    "install-sh", SkillsInstallStepState.Skipped, null, null, "bash not available"));
            }
            else
            {
                var cacheStopwatch = Stopwatch.StartNew();
                var cacheReady = false;
                try
                {
                    var prepare = await SkillsRepository
                        .EnsureCacheAsync(_cacheDirectory, repository, token, GitExec, _logger, cancellationToken)
                        .ConfigureAwait(false);
                    if (prepare == CachePrepareResult.Recovered)
                    {
                        steps.Add(new SkillsInstallStep(
                            "cache-prepare", SkillsInstallStepState.Succeeded, null,
                            cacheStopwatch.ElapsedMilliseconds,
                            "recovered from inaccessible cache — stale clone moved aside"));
                    }

                    cacheReady = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skills cache refresh failed for {Repository}.", repository);
                    steps.Add(new SkillsInstallStep(
                        "cache-prepare", SkillsInstallStepState.Failed, null, cacheStopwatch.ElapsedMilliseconds,
                        Sanitize(ex.Message)));
                }

                if (cacheReady)
                {
                    var script = Path.Join(_cacheDirectory, "install.sh");
                    steps.Add(!File.Exists(script)
                        ? new SkillsInstallStep(
                            "install-sh", SkillsInstallStepState.Skipped, null, null, "no install.sh in repository")
                        : await RunStepAsync(
                            "install-sh", bash, _cacheDirectory, ["install.sh", "--all"], cancellationToken)
                            .ConfigureAwait(false));
                }
                else
                {
                    steps.Add(new SkillsInstallStep(
                        "install-sh", SkillsInstallStepState.Skipped, null, null,
                        "cache-prepare failed"));
                }
            }

            foreach (var step in steps)
            {
                var line = $"Step {step.Name}: {step.State}";
                if (step.State == SkillsInstallStepState.Failed)
                {
                    _log?.Error($"{line} — {Sanitize(step.Message)}");
                }
                else
                {
                    _log?.Info(step.Message is null ? line : $"{line} — {step.Message}");
                }
            }

            _state = steps.Any(s => s.State == SkillsInstallStepState.Failed)
                ? SkillsSyncState.Failed
                : SkillsSyncState.Succeeded;
            if (_state == SkillsSyncState.Failed)
            {
                error = steps
                    .FirstOrDefault(s => s.State == SkillsInstallStepState.Failed)
                    ?.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skills install failed for repository {Repository}.", repository);
            _state = SkillsSyncState.Failed;
            error = Sanitize(ex.Message);
            _log?.Error($"Skills install failed: {error}");
        }

        var locations = ScanLocations();
        var total = locations.Sum(l => l.Count);
        _log?.Info(
            $"Skills install finished: {_state} — {total} skill(s) in " +
            $"{locations.Count} location(s), {stopwatch.ElapsedMilliseconds} ms.");

        _lastRun = new LastRun(started, stopwatch.ElapsedMilliseconds, repository, steps, error);
        new InstallManifest
        {
            Repository = repository,
            InstalledAtUtc = started,
            DurationMs = stopwatch.ElapsedMilliseconds,
            SkillCount = total,
            State = _state.ToString(),
            NpxStep = steps.FirstOrDefault(s => s.Name == "npx-add"),
            InstallShStep = steps.FirstOrDefault(s => s.Name == "install-sh")
        }.Save(_manifestPath);

        return GetStatus();
    }

    private async Task<SkillsInstallStep> RunStepAsync(
        string name,
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StepTimeout);
            var result = await _runner
                .RunAsync(executable, workingDirectory, arguments, timeout.Token)
                .ConfigureAwait(false);

            return result.ExitCode == 0
                ? new SkillsInstallStep(name, SkillsInstallStepState.Succeeded, 0, stopwatch.ElapsedMilliseconds, null)
                : new SkillsInstallStep(
                    name, SkillsInstallStepState.Failed, result.ExitCode, stopwatch.ElapsedMilliseconds,
                    Sanitize(result.StdErr));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SkillsInstallStep(
                name, SkillsInstallStepState.Failed, null, stopwatch.ElapsedMilliseconds, "Step timed out.");
        }
        catch (Exception ex)
        {
            return new SkillsInstallStep(
                name, SkillsInstallStepState.Failed, null, stopwatch.ElapsedMilliseconds, Sanitize(ex.Message));
        }
    }

    private async Task<GitResult> GitExec(
        string workingDirectory,
        string? authToken,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var git = _locator("git")
            ?? throw new InvalidOperationException("git executable was not found on PATH.");

        var arguments = new List<string>();
        if (!string.IsNullOrEmpty(authToken))
        {
            var basic = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"x-access-token:{authToken}"));
            arguments.Add("-c");
            arguments.Add($"http.https://github.com/.extraheader=AUTHORIZATION: basic {basic}");
        }

        arguments.AddRange(args);

        var result = await _runner.RunAsync(git, workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);

        var stderr = result.StdErr;
        if (!string.IsNullOrEmpty(authToken))
        {
            stderr = stderr.Replace(authToken, "***", StringComparison.Ordinal);
        }

        return new GitResult(result.ExitCode, result.StdOut, stderr);
    }

    private List<SkillsInstallLocation> ScanLocations()
    {
        var directories = Enum.GetValues<AgentType>()
            .SelectMany(type => AgentSkillDirectoryMap.GetSkillDirectories(type, _homeDirectory))
            .Append(Path.Join(_homeDirectory, ".agents", "skills"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var locations = new List<SkillsInstallLocation>();
        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var count = Directory.EnumerateDirectories(directory)
                    .Count(sub => HasValidSkillFile(sub));
                if (count > 0)
                {
                    locations.Add(new SkillsInstallLocation(directory, count));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not scan skills directory {Directory}.", directory);
            }
        }

        return locations;
    }

    private static bool HasValidSkillFile(string skillDirectory)
    {
        var skillFile = Path.Join(skillDirectory, "SKILL.md");
        return File.Exists(skillFile) && FrontmatterReader.Read(skillFile) is not null;
    }

    private IReadOnlyDictionary<string, bool> DetectPrerequisites() =>
        RequiredTools.Concat(OptionalTools)
            .ToDictionary(tool => tool, tool => _locator(tool) is not null);

    private string? SafeResolveRepository()
    {
        try
        {
            return SkillsRepository.Resolve(_configuration);
        }
        catch
        {
            return null;
        }
    }

    private string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Command failed without output.";
        }

        var sanitized = message.Replace(_homeDirectory, "~", StringComparison.Ordinal);
        return sanitized.Length <= 500 ? sanitized : sanitized[^500..];
    }
}
