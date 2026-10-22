using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Integrations.Agents;

namespace Taskboard.Integrations.Execution;

/// <summary>
/// SPEC-20261020-agent-process-isolation RF-001: wraps spawns in
/// <c>systemd-run --user --scope --collect</c> so each agent/PTY child gets
/// a transient cgroup with its own <c>MemoryMax</c>/<c>MemoryHigh</c>/
/// <c>OOMScoreAdjust</c> — an OOM kill stays inside the scope and the
/// server unit survives. Availability is probed once at construction;
/// per-spawn failure is impossible here because wrapping never executes
/// code — a systemd-run failure at <see cref="Process.Start"/> behaves like
/// any spawn error the caller already handles.
/// </summary>
public sealed class SystemdScopeProcessIsolator : IProcessIsolator
{
    private readonly ILogger _logger;
    private readonly Func<string, string?> _locator;
    private readonly string? _systemdRun;
    private readonly string _memoryMax;
    private readonly string _memoryHigh;
    private readonly int _oomScoreAdjust;
    private readonly string _scopePrefix;
    private bool _unavailableLogged;

    public SystemdScopeProcessIsolator(
        IConfiguration? configuration,
        ILogger logger,
        Func<string, string?>? executableLocator = null)
    {
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        var section = configuration?.GetSection("Harness:ProcessIsolation");
        var enabled = section?.GetValue<bool?>("Enabled") ?? true;
        _memoryMax = section?["MemoryMax"] ?? "2G";
        _memoryHigh = section?["MemoryHigh"] ?? "1.5G";
        _oomScoreAdjust = section?.GetValue<int?>("OomScoreAdjust") ?? 500;
        _scopePrefix = section?["ScopePrefix"] ?? "harness-agent";

        if (!enabled || !OperatingSystem.IsLinux())
        {
            _systemdRun = null;
        }
        else
        {
            var binary = _locator("systemd-run");
            // A user manager is required for --user scopes: XDG_RUNTIME_DIR
            // + the user manager bus must exist.
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var userManagerAvailable =
                !string.IsNullOrWhiteSpace(runtimeDir)
                && Directory.Exists(Path.Combine(runtimeDir, "systemd"));
            _systemdRun = binary is not null && userManagerAvailable ? binary : null;
            if (_systemdRun is null)
            {
                _logger.LogWarning(
                    "Process isolation unavailable (systemd-run or user manager missing) — spawning directly.");
                _unavailableLogged = true;
            }
        }
    }

    /// <summary>False when isolation is disabled or the host lacks systemd user scopes.</summary>
    public bool IsAvailable => _systemdRun is not null;

    /// <inheritdoc />
    public ProcessStartInfo Isolate(ProcessStartInfo startInfo, string scopeHint)
    {
        if (_systemdRun is null)
        {
            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                _logger.LogWarning(
                    "Process isolation unavailable — spawning {File} directly.", startInfo.FileName);
            }
            return startInfo;
        }

        var scopeName = $"{_scopePrefix}-{Sanitize(scopeHint)}-{Guid.NewGuid():N}";
        if (scopeName.Length > 64)
        {
            scopeName = scopeName[..64];
        }
        var isolated = new ProcessStartInfo(_systemdRun)
        {
            WorkingDirectory = startInfo.WorkingDirectory,
            RedirectStandardInput = startInfo.RedirectStandardInput,
            RedirectStandardOutput = startInfo.RedirectStandardOutput,
            RedirectStandardError = startInfo.RedirectStandardError,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (startInfo.RedirectStandardOutput)
        {
            isolated.StandardOutputEncoding = startInfo.StandardOutputEncoding;
        }
        if (startInfo.RedirectStandardInput)
        {
            isolated.StandardInputEncoding = startInfo.StandardInputEncoding;
        }

        foreach (var (key, value) in startInfo.Environment)
        {
            isolated.Environment[key] = value;
        }

        isolated.ArgumentList.Add("--user");
        isolated.ArgumentList.Add("--scope");
        isolated.ArgumentList.Add("--collect");
        isolated.ArgumentList.Add("--quiet");
        isolated.ArgumentList.Add($"--unit={scopeName}");
        isolated.ArgumentList.Add($"-p");
        isolated.ArgumentList.Add($"MemoryMax={_memoryMax}");
        isolated.ArgumentList.Add($"-p");
        isolated.ArgumentList.Add($"MemoryHigh={_memoryHigh}");
        isolated.ArgumentList.Add($"-p");
        isolated.ArgumentList.Add($"OOMScoreAdjust={_oomScoreAdjust}");
        isolated.ArgumentList.Add("--");
        isolated.ArgumentList.Add(startInfo.FileName);
        foreach (var arg in startInfo.ArgumentList)
        {
            isolated.ArgumentList.Add(arg);
        }

        _logger.LogInformation(
            "Spawning {File} inside scope {Scope} (MemoryMax={Max} MemoryHigh={High} OOMScoreAdjust={Adj})",
            startInfo.FileName, scopeName, _memoryMax, _memoryHigh, _oomScoreAdjust);
        return isolated;
    }

    private static string Sanitize(string hint)
    {
        var span = hint.AsSpan();
        var buffer = new char[Math.Min(span.Length, 24)];
        var n = 0;
        foreach (var c in span)
        {
            if (n >= buffer.Length)
            {
                break;
            }
            buffer[n++] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-';
        }
        return n == 0 ? "x" : new string(buffer, 0, n);
    }
}
