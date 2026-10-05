using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Mcp;

/// <summary>
/// One MCP server entry found inside an agent CLI's config file.
/// <see cref="Transport"/> is <c>"http"</c>/<c>"stdio"</c>;
/// <see cref="Detail"/> is the URL or the command line.
/// </summary>
public sealed record AgentMcpServerEntryDto(
    string Name,
    string Transport,
    string Detail);

/// <summary>
/// Per-agent inventory row (SPEC-20261010-mcp-skills-hub RF-004):
/// the resolved config path plus every MCP entry it holds. Agents whose
/// config format is CLI-managed (Antigravity/Cline) or directory-based
/// (Continue) still report their entries; <see cref="Writable"/> is false for
/// agents the provisioning service cannot write safely (kept RAG-only).
/// </summary>
public sealed record AgentMcpInventoryDto(
    AgentType Agent,
    string ConfigPath,
    bool Writable,
    IReadOnlyList<AgentMcpServerEntryDto> Servers);

/// <summary>Install payload — one spec applied to a set of agent CLIs.</summary>
public sealed record AgentMcpInstallRequest(
    string Name,
    string? Url,
    string? Command,
    IReadOnlyList<string>? Args,
    IReadOnlyDictionary<string, string>? Headers,
    IReadOnlyDictionary<string, string>? Env,
    IReadOnlyList<AgentType> Agents);

/// <summary>Remove payload — drop the named entry from the given agents.</summary>
public sealed record AgentMcpRemoveRequest(
    string Name,
    IReadOnlyList<AgentType> Agents);
