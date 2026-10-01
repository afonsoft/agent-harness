using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20260928-agent-cli-probe-background: single owner of the expensive
/// CLI probes (<c>--version</c>, <c>&lt;cli&gt; models</c>). Keeps the last-known
/// results in memory and on disk (<c>agent-probe-snapshot.json</c>) and refreshes
/// them in the background with parallel subprocess probes (single-flight).
/// Cheap checks — PATH lookup and credential-file existence — stay inline in
/// the readers; this service only owns subprocess-derived data.
/// </summary>
public sealed class CliProbeSnapshotService
{
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ModelsProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex VersionPattern = new(@"\d+\.\d+(\.\d+)?", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _snapshotFile;
    private readonly string _homeDirectory;
    private readonly ILogger<CliProbeSnapshotService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly ISkillsInstallRunner _runner;
    private readonly TimeProvider _time;

    private readonly ConcurrentDictionary<AgentCliKind, string> _versions = new();
    private readonly ConcurrentDictionary<AgentType, IReadOnlyList<string>> _models = new();
    private readonly object _refreshGate = new();
    private readonly object _saveGate = new();
    private Task? _refreshTask;

    public CliProbeSnapshotService(
        string snapshotFile,
        string homeDirectory,
        ILogger<CliProbeSnapshotService> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null,
        TimeProvider? timeProvider = null)
    {
        _snapshotFile = snapshotFile;
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
        _time = timeProvider ?? TimeProvider.System;
        LoadSnapshot();
    }

    /// <summary>Whether a background refresh is currently in flight.</summary>
    public bool Refreshing { get; private set; }

    /// <summary>When the last refresh finished; <see cref="DateTimeOffset.MinValue"/> if never.</summary>
    public DateTimeOffset LastCompletedAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Wall-clock duration of the last completed refresh.</summary>
    public TimeSpan LastDuration { get; private set; }

    /// <summary>
    /// Error of the last refresh when it failed wholesale (timeout/exception);
    /// null after a run that reached completion. Individual probe failures stay
    /// absorbed — a missing CLI is a normal condition.
    /// </summary>
    public string? LastRefreshError { get; private set; }

    /// <summary>Last-known CLI version, or null when never probed.</summary>
    public string? GetVersion(AgentCliKind kind) =>
        _versions.TryGetValue(kind, out var version) ? version : null;

    /// <summary>Last-known model list; null when never probed (vs. probed-empty = <c>[]</c>).</summary>
    public IReadOnlyList<string>? GetModels(AgentType type) =>
        _models.TryGetValue(type, out var models) ? models : null;

    /// <summary>
    /// Records a model list produced by a synchronous forced probe and
    /// persists it immediately — a manual Sync must survive a restart instead
    /// of reverting to the last background snapshot (SPEC-20260929 RF-002).
    /// </summary>
    public void SetModels(AgentType type, IReadOnlyList<string> models)
    {
        _models[type] = models;
        LastCompletedAt = _time.GetUtcNow();
        SaveSnapshot(LastCompletedAt);
    }

    /// <summary>
    /// Kicks a background refresh when none is running (single-flight).
    /// Returns <c>true</c> when a new refresh was started, <c>false</c> when one
    /// is already in flight — callers join the same underlying work.
    /// </summary>
    public bool EnsureRefreshing()
    {
        lock (_refreshGate)
        {
            if (Refreshing)
            {
                return false;
            }

            Refreshing = true;
            _refreshTask = RunRefreshAsync();
            return true;
        }
    }

    /// <summary>Starts (or joins) a background refresh; completes when the probes finish.</summary>
    public Task RefreshAsync()
    {
        EnsureRefreshing();
        lock (_refreshGate)
        {
            return _refreshTask ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fires a background refresh when the snapshot is older than <paramref name="ttl"/>.
    /// A non-positive TTL disables the staleness trigger (manual/startup refresh only).
    /// </summary>
    public void EnsureFresh(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        if (_time.GetUtcNow() - LastCompletedAt > ttl)
        {
            EnsureRefreshing();
        }
    }

    private async Task RunRefreshAsync()
    {
        var started = _time.GetUtcNow();
        LastRefreshError = null;
        try
        {
            using var timeout = new CancellationTokenSource(RefreshTimeout);
            var tasks = new List<Task>();

            foreach (var (kind, spec) in AgentCliMap.AllSpecs())
            {
                var path = _locator(spec.Binary);
                if (path is not null)
                {
                    tasks.Add(ProbeVersionAsync(kind, path, timeout.Token));
                }
            }

            foreach (var type in Enum.GetValues<AgentType>())
            {
                var probe = AgentCliModels.ModelListProbe(type);
                var kind = AgentCliMap.CliKindFor(type);
                var binary = kind is null ? null : AgentCliMap.GetSpec(kind.Value)?.Binary;
                var path = binary is null ? null : _locator(binary);
                if (probe is not null && path is not null)
                {
                    tasks.Add(ProbeModelsAsync(type, path, probe, timeout.Token));
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            var completed = _time.GetUtcNow();
            LastCompletedAt = completed;
            LastDuration = completed - started;
            SaveSnapshot(completed);
        }
        catch (OperationCanceledException ex)
        {
            LastRefreshError = $"timed out after {RefreshTimeout}";
            _logger.LogWarning(ex, "CLI probe refresh timed out after {Timeout}.", RefreshTimeout);
        }
        catch (Exception ex)
        {
            LastRefreshError = ex.Message;
            _logger.LogWarning(ex, "CLI probe refresh failed.");
        }
        finally
        {
            lock (_refreshGate)
            {
                Refreshing = false;
                _refreshTask = null;
            }
        }
    }

    private async Task ProbeVersionAsync(AgentCliKind kind, string binaryPath, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VersionProbeTimeout);
            var result = await _runner
                .RunAsync(binaryPath, _homeDirectory, ["--version"], timeout.Token)
                .ConfigureAwait(false);

            var output = (result.StdOut + "\n" + result.StdErr).Trim();
            if (output.Length == 0)
            {
                return;
            }

            var match = VersionPattern.Match(output);
            var version = match.Success ? match.Value : output.Split('\n')[0].Trim();
            _versions[kind] = version.Length <= 40 ? version : version[..40];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Version probe failed for {Kind} ({Binary}).", kind, binaryPath);
        }
    }

    private async Task ProbeModelsAsync(
        AgentType type, string binaryPath, AgentModelListProbe probe, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ModelsProbeTimeout);
            var result = await _runner
                .RunAsync(binaryPath, _homeDirectory, probe.Arguments, timeout.Token)
                .ConfigureAwait(false);

            var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
            _models[type] = AgentModelListParser.Parse(probe.Format, output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Model-list probe failed for {AgentType} ({Binary}).", type, binaryPath);
        }
    }

    private void LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_snapshotFile))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<ProbeSnapshot>(File.ReadAllText(_snapshotFile));
            if (snapshot is null)
            {
                return;
            }

            foreach (var (name, version) in snapshot.Versions)
            {
                if (Enum.TryParse<AgentCliKind>(name, ignoreCase: true, out var kind))
                {
                    _versions[kind] = version;
                }
            }

            foreach (var (name, models) in snapshot.Models)
            {
                if (Enum.TryParse<AgentType>(name, ignoreCase: true, out var type))
                {
                    _models[type] = models;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignoring corrupt CLI probe snapshot at {File}.", _snapshotFile);
        }
    }

    private void SaveSnapshot(DateTimeOffset savedAt)
    {
        // Serialized — a forced SetModels may run concurrently with the
        // background refresh writer (SPEC-20260929 RF-002).
        lock (_saveGate)
        {
            try
            {
                var snapshot = new ProbeSnapshot(
                    savedAt,
                    _versions.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    _models.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));

                var directory = Path.GetDirectoryName(_snapshotFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Atomic write: temp file + rename so a crash never leaves a partial JSON.
                var tempFile = _snapshotFile + ".tmp";
                File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot, JsonOptions));
                File.Move(tempFile, _snapshotFile, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist CLI probe snapshot at {File}.", _snapshotFile);
            }
        }
    }

    private sealed record ProbeSnapshot(
        [property: JsonPropertyName("savedAt")] DateTimeOffset SavedAt,
        [property: JsonPropertyName("versions")] Dictionary<string, string> Versions,
        [property: JsonPropertyName("models")] Dictionary<string, IReadOnlyList<string>> Models);
}
