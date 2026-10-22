using System.Diagnostics;

namespace Taskboard.Integrations.Execution;

/// <summary>
/// SPEC-20261020-agent-process-isolation RF-001/RF-002: wraps agent/PTY
/// spawn requests so they run inside their own resource scope instead of
/// the server's cgroup. Implementations must never break the spawn —
/// fallback is a pass-through.
/// </summary>
public interface IProcessIsolator
{
    /// <summary>
    /// Returns a <see cref="ProcessStartInfo"/> that launches the requested
    /// command inside an isolated scope when isolation is available, or the
    /// original <paramref name="startInfo"/> unchanged when it is not.
    /// </summary>
    /// <param name="startInfo">Prepared spawn (argv/env/workdir already set).</param>
    /// <param name="scopeHint">Short label for the scope name (e.g. "pty", "agent-run").</param>
    ProcessStartInfo Isolate(ProcessStartInfo startInfo, string scopeHint);
}
