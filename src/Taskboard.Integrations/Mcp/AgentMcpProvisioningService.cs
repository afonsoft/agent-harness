using Microsoft.Extensions.Logging;

using Taskboard.Agents;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Mcp;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Writes an arbitrary MCP server entry into the user-scope config file of the
/// selected agent CLIs (SPEC-20261010-mcp-skills-hub RF-003) — the generalized
/// sibling of the RAG-only <see cref="McpProvisioningService"/>. Shares
/// <see cref="McpConfigFileWriter"/> (idempotent merge, atomic temp+rename,
/// <c>.bak</c>, <c>0600</c>).
///
/// Agents whose config is CLI-owned (<see cref="AgentType.Antigravity"/>,
/// <see cref="AgentType.Cline"/>) or directory-based
/// (<see cref="AgentType.Continue"/>) stay RAG-only in this phase and report
/// <see cref="McpAgentState.Skipped"/>.
/// </summary>
public sealed class AgentMcpProvisioningService
{
    /// <summary>Agents the writer knows how to edit safely.</summary>
    private static readonly HashSet<McpEntryStyle> WritableStyles =
    [
        McpEntryStyle.Devin,
        McpEntryStyle.Claude,
        McpEntryStyle.OpenCode,
        McpEntryStyle.OpenHands,
        McpEntryStyle.Codex,
        McpEntryStyle.Kimi,
        McpEntryStyle.Qwen,
        McpEntryStyle.Copilot,
        McpEntryStyle.Kiro
    ];

    private readonly string _homeDirectory;
    private readonly ILogger<AgentMcpProvisioningService> _logger;
    private readonly McpOperationLog? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentMcpProvisioningService(
        string homeDirectory,
        ILogger<AgentMcpProvisioningService> logger,
        McpOperationLog? log = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _log = log;
    }

    /// <summary>Agents that accept arbitrary entries (config target + writable style).</summary>
    public static bool IsWritable(AgentType agent)
    {
        var target = AgentMcpConfigMap.GetTarget(agent);
        return target is not null && WritableStyles.Contains(target.Style);
    }

    /// <summary>
    /// Applies <paramref name="spec"/> to every agent in
    /// <paramref name="agents"/> — or removes the entry named
    /// <see cref="ChatMcpServerSpec.Name"/> when <paramref name="remove"/> is
    /// set (the rest of the spec is ignored). Returns one
    /// <see cref="McpAgentResult"/> per agent; never throws for per-agent
    /// failures.
    /// </summary>
    public async Task<IReadOnlyList<McpAgentResult>> ApplyAsync(
        ChatMcpServerSpec spec,
        IReadOnlyCollection<AgentType> agents,
        bool remove,
        CancellationToken cancellationToken)
    {
        var results = new List<McpAgentResult>();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _log?.Info(
                $"Agent MCP {(remove ? "remove" : "install")} started — '{spec.Name}', " +
                $"{agents.Count} target(s).");
            foreach (var agent in agents.Distinct())
            {
                var result = ApplyAgent(agent, spec, remove);
                results.Add(result);
                var line = $"{agent}: {result.State} — {result.Path}";
                if (result.State == McpAgentState.Failed)
                {
                    _log?.Error($"{line} — {result.Error}");
                }
                else
                {
                    _log?.Info(line);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        _log?.Info($"Agent MCP {(remove ? "remove" : "install")} finished.");
        return results;
    }

    private McpAgentResult ApplyAgent(AgentType agent, ChatMcpServerSpec spec, bool remove)
    {
        var target = AgentMcpConfigMap.GetTarget(agent);
        if (target is null)
        {
            return new McpAgentResult(
                agent, false, string.Empty, null, McpAgentState.Skipped,
                "no known MCP config target");
        }

        if (!WritableStyles.Contains(target.Style))
        {
            return new McpAgentResult(
                agent, false, AgentMcpConfigMap.GetConfigPath(target, _homeDirectory),
                null, McpAgentState.Skipped,
                "config format is managed by the agent CLI — RAG-only for now");
        }

        var path = AgentMcpConfigMap.GetConfigPath(target, _homeDirectory);
        try
        {
            var outcome = McpConfigFileWriter.Apply(path, target, spec.Name, remove ? null : spec);
            var state = outcome switch
            {
                MergeOutcome.Repaired => McpAgentState.Repaired,
                MergeOutcome.Removed => McpAgentState.Removed,
                MergeOutcome.Updated => McpAgentState.Updated,
                MergeOutcome.NoChange when remove => McpAgentState.NotConfigured,
                _ => McpAgentState.Configured
            };
            return new McpAgentResult(
                agent, !remove, path,
                remove ? null : (string.IsNullOrWhiteSpace(spec.Command) ? "http" : "stdio"),
                state, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent MCP write failed for {Agent} at {Path}.", agent, path);
            return new McpAgentResult(
                agent, false, path, null, McpAgentState.Failed, Sanitize(ex.Message));
        }
    }

    private string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Operation failed without output.";
        }

        var sanitized = message.Replace(_homeDirectory, "~", StringComparison.Ordinal);
        return sanitized.Length <= 500 ? sanitized : sanitized[^500..];
    }
}
