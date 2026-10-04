namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-001: one slash command (or CLI-scoped
/// skill) discovered under a CLI's conventional directories. <paramref name="Kind"/>
/// is <c>"command"</c> for file-backed commands and <c>"skill"</c> for SKILL.md
/// entries under the CLI's skills dir; <paramref name="Source"/> is the cli
/// label shown in the palette badge (e.g. "claude", "opencode").
/// </summary>
public sealed record CliCommandDto(
    string Name,
    string Description,
    string Kind,
    string Source);

/// <summary>Command/skill body for chat injection (RF-003).</summary>
public sealed record CliCommandDetailDto(
    string Name,
    string Description,
    string Kind,
    string Source,
    string Body,
    string? ArgumentHint);

/// <summary>
/// Discovers slash commands and skills for a specific agent CLI. Accepts the
/// <see cref="Taskboard.Agents.AgentCliKind"/> or <see cref="Taskboard.Agents.AgentType"/>
/// name in <paramref name="cli"/> (case-insensitive); unknown/unsupported CLIs
/// yield empty results.
/// </summary>
public interface ICliCommandDiscoveryService
{
    Task<IReadOnlyList<CliCommandDto>> ListAsync(string cli, CancellationToken cancellationToken = default);

    Task<CliCommandDetailDto?> GetAsync(string cli, string name, CancellationToken cancellationToken = default);
}
