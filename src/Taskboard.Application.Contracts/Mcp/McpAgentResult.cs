using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Mcp;

/// <summary>Per-agent outcome of a provision pass or status read.</summary>
public enum McpAgentState
{
    /// <summary>Managed entry present with the configured URL.</summary>
    Configured,

    /// <summary>Managed entry existed with a different value and was overwritten.</summary>
    Updated,

    /// <summary>Managed entry was removed (URL cleared).</summary>
    Removed,

    /// <summary>Config file was corrupt — backed up to .corrupt-bak and rewritten.</summary>
    Repaired,

    /// <summary>Agent has no known config target (unknown/future enum member).</summary>
    Skipped,

    /// <summary>Managed entry absent.</summary>
    NotConfigured,

    /// <summary>Read/write failed; see <see cref="McpAgentResult.Error"/>.</summary>
    Failed
}

/// <summary>Result for one agent config file. Never contains the API key.</summary>
public sealed record McpAgentResult(
    AgentType Agent,
    bool Configured,
    string Path,
    string? Transport,
    McpAgentState State,
    string? Error);
