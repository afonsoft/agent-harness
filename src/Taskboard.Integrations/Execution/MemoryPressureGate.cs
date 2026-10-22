using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Execution;

/// <summary>
/// SPEC-20261020-agent-process-isolation RF-004: PSI memory-pressure gate.
/// Reads the service cgroup's <c>memory.pressure</c> — path from the
/// <c>MEMORY_PRESSURE_WATCH</c> env var when set, else
/// <c>Harness:MemoryPressure:PressureFile</c>, else resolved from
/// <c>/proc/self/cgroup</c>. Fail-open: unreadable/absent file allows the
/// operation (a single Warn is logged once).
/// </summary>
public sealed class MemoryPressureGate
{
    private readonly ILogger<MemoryPressureGate> _logger;
    private readonly string? _pressureFile;
    private readonly double _someAvg60Max;
    private bool _readFailureLogged;
    private bool _wasOver;

    public MemoryPressureGate(IConfiguration? configuration, ILogger<MemoryPressureGate> logger)
    {
        _logger = logger;
        var section = configuration?.GetSection("Harness:MemoryPressure");
        _someAvg60Max = section?.GetValue<double?>("SomeAvg60Max") ?? 30.0;
        _pressureFile = section?["PressureFile"]
            ?? Environment.GetEnvironmentVariable("MEMORY_PRESSURE_WATCH")
            ?? ResolveOwnCgroupPressureFile();
    }

    /// <summary>Test seam: explicit pressure file + threshold, no env probing.</summary>
    internal MemoryPressureGate(string? pressureFile, double someAvg60Max, ILogger<MemoryPressureGate> logger)
    {
        _logger = logger;
        _pressureFile = pressureFile;
        _someAvg60Max = someAvg60Max;
    }

    /// <summary>Configured threshold for <c>some avg60</c> (default 30.00).</summary>
    public double SomeAvg60Max => _someAvg60Max;

    /// <summary>
    /// Current <c>some avg60</c> value, or null when the pressure file is
    /// absent/unreadable/unparsable (fail-open by design).
    /// </summary>
    public double? SampleSomeAvg60()
    {
        if (_pressureFile is null || !File.Exists(_pressureFile))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(_pressureFile))
            {
                // line shape: "some avg10=0.00 avg60=0.00 avg300=0.00 total=0"
                if (!line.StartsWith("some", StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith("avg60=", StringComparison.Ordinal)
                        && double.TryParse(
                            token["avg60=".Length..],
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_readFailureLogged)
            {
                _readFailureLogged = true;
                _logger.LogWarning(ex, "memory.pressure unreadable at {Path} — gate stays open.", _pressureFile);
            }
            return null;
        }

        if (!_readFailureLogged)
        {
            _readFailureLogged = true;
            _logger.LogWarning(
                "memory.pressure at {Path} has no parsable 'some avg60' — gate stays open.", _pressureFile);
        }
        return null;
    }

    /// <summary>
    /// True when a new terminal/agent session may be created. Over-threshold
    /// pressure logs Warn once per crossing — callers map the refusal.
    /// </summary>
    public bool Allow(out double? sampled)
    {
        sampled = SampleSomeAvg60();
        if (sampled is not { } value || value <= _someAvg60Max)
        {
            _wasOver = false;
            return true;
        }

        // Warn once per rising edge — callers poll this each dispatch tick.
        if (!_wasOver)
        {
            _wasOver = true;
            _logger.LogWarning(
                "Refusing new sessions: memory.pressure some avg60={Value:F2} > {Max:F2} ({Path})",
                value, _someAvg60Max, _pressureFile);
        }
        return false;
    }

    /// <summary>
    /// Diagnostics snapshot for the ops surface: cgroup memory counters +
    /// last PSI sample. Null fields mean unreadable.
    /// </summary>
    public MemoryPressureSnapshot Snapshot()
    {
        var cgroupDir = _pressureFile is not null
            ? Path.GetDirectoryName(_pressureFile)
            : null;
        return new MemoryPressureSnapshot(
            PressureFile: _pressureFile,
            SomeAvg60: SampleSomeAvg60(),
            SomeAvg60Max: _someAvg60Max,
            CurrentBytes: ReadCgroupLong(cgroupDir, "memory.current"),
            HighBytes: ReadCgroupLong(cgroupDir, "memory.high"),
            MaxBytes: ReadCgroupLong(cgroupDir, "memory.max"),
            Events: ReadCgroupEvents(cgroupDir));
    }

    private static long? ReadCgroupLong(string? dir, string name)
    {
        if (dir is null)
        {
            return null;
        }
        try
        {
            var text = File.ReadAllText(Path.Combine(dir, name)).Trim();
            return long.TryParse(text, out var v) ? v : null; // "max" → null
        }
        catch (Exception) { return null; }
    }

    private static IReadOnlyDictionary<string, long>? ReadCgroupEvents(string? dir)
    {
        if (dir is null)
        {
            return null;
        }
        try
        {
            var events = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(dir, "memory.events")))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && long.TryParse(parts[1], out var v))
                {
                    events[parts[0]] = v;
                }
            }
            return events;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Own cgroup pressure file: /proc/self/cgroup → /sys/fs/cgroup/…/memory.pressure.</summary>
    private static string? ResolveOwnCgroupPressureFile()
    {
        try
        {
            if (!OperatingSystem.IsLinux() || !File.Exists("/proc/self/cgroup"))
            {
                return null;
            }
            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                var idx = line.IndexOf("::", StringComparison.Ordinal);
                var rel = idx >= 0 ? line[(idx + 2)..] : line;
                var candidate = Path.Combine("/sys/fs/cgroup", rel.TrimStart('/'), "memory.pressure");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception) { /* fail-open */ }
        return null;
    }
}

/// <summary>Ops-visible snapshot of the cgroup memory state.</summary>
public sealed record MemoryPressureSnapshot(
    string? PressureFile,
    double? SomeAvg60,
    double SomeAvg60Max,
    long? CurrentBytes,
    long? HighBytes,
    long? MaxBytes,
    IReadOnlyDictionary<string, long>? Events);
