using System.Diagnostics;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261025 C2: second-layer exec sandbox for confined chat tools.
/// When enabled and available, <see cref="Wrap"/> rewrites the
/// <see cref="ProcessStartInfo"/> so the command runs inside the sandbox
/// (e.g. a docker container) instead of on the host. The risk-tier/approval
/// pipeline is untouched — sandboxing layers on top of classification,
/// never replaces it. When <see cref="IsAvailableAsync"/> reports false the
/// caller falls back to host exec and surfaces a visible warning.
/// </summary>
public interface IChatToolSandbox
{
    /// <summary>Human-readable label for warnings/diagnostics (e.g. "docker (image)").</summary>
    string Name { get; }

    /// <summary>Configured mode — false when the operator turned the sandbox off.</summary>
    bool Enabled { get; }

    /// <summary>Reachability probe (docker socket etc.) — cached by the implementation.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites <paramref name="startInfo"/> in place so the original
    /// fileName/argv execute inside the sandbox (mounting WorkingDirectory).
    /// </summary>
    void Wrap(ProcessStartInfo startInfo);
}
