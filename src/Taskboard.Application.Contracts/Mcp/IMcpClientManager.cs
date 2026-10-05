using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Contracts.Mcp;

/// <summary>
/// A chat-facing MCP server definition (SPEC-20261001-chat-mcp-client FR-001).
/// Reuses the provisioning spec shape (name/url or command/args + headers/env);
/// values of the form <c>env:VAR</c> are resolved server-side from the process
/// environment and are never serialized into capability payloads.
/// <see cref="Origin"/> records where the spec came from —
/// <c>"config"</c> (<c>Taskboard:Chat:Mcp:Servers</c>),
/// <c>"agents-global"</c> (<c>~/.agents</c>, SPEC-20261010-mcp-skills-hub) or
/// <c>"rag"</c> (the provisioned RAG server).
/// </summary>
public sealed record ChatMcpServerSpec(
    string Name,
    string? Url = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyDictionary<string, string>? Env = null,
    string Origin = "config");

/// <summary>Per-server health entry surfaced in Settings → MCP/Skills.</summary>
public sealed record ChatMcpServerStatus(
    string Name,
    string Transport,
    bool Healthy,
    int ToolCount,
    string? Error,
    string Origin = "config");

/// <summary>
/// Chat-side MCP client bridge (SPEC-20261001-chat-mcp-client FR-002).
/// Singleton — owns the connected sessions, exposes discovered tools as
/// <see cref="IChatTool"/> adapters named <c>mcp_{server}_{tool}</c> with
/// capability ids <c>mcp:{server}/{tool}</c>, and dispatches calls with
/// timeout/redaction. Inert when <c>Taskboard:Chat:Mcp:Enabled=false</c>.
/// </summary>
public interface IMcpClientManager : IAsyncDisposable
{
    /// <summary>Configured servers + last connect health. Empty when disabled.</summary>
    IReadOnlyList<ChatMcpServerStatus> GetServers();

    /// <summary>
    /// Lazily connects all enabled servers (parallel, bounded) and returns the
    /// discovered tool adapters. Connection failures degrade to an unhealthy
    /// status — never throw.
    /// </summary>
    Task<IReadOnlyList<IChatTool>> GetToolsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Invokes a remote tool. Reports <c>running_mcp</c> activity, enforces the
    /// call timeout, retries once on transport failure, redacts and truncates
    /// output, and converts MCP errors into <see cref="ChatToolResult"/> error
    /// payloads — never leaks secrets, never throws for remote failures.
    /// </summary>
    Task<ChatToolResult> CallAsync(
        string serverName,
        string remoteToolName,
        JsonElement arguments,
        ChatToolContext context,
        CancellationToken cancellationToken);
}
