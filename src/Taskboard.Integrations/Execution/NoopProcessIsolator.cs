using System.Diagnostics;

namespace Taskboard.Integrations.Execution;

/// <summary>Pass-through isolator — direct spawn, used when cgroup
/// isolation is unavailable or disabled (SPEC-20261020 RF-002).</summary>
public sealed class NoopProcessIsolator : IProcessIsolator
{
    public static readonly NoopProcessIsolator Instance = new();

    public ProcessStartInfo Isolate(ProcessStartInfo startInfo, string scopeHint) => startInfo;
}
