using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261025 C4: one live LSP session bound to a workspace root —
/// JSON-RPC over stdio. Implementations serialize requests and correlate
/// responses by id; notifications are drained by the read loop.
/// </summary>
public interface ILspClient
{
    /// <summary>Handshake — <c>initialize</c> + <c>initialized</c> notification.</summary>
    Task InitializeAsync(string rootUri, CancellationToken cancellationToken);

    /// <summary>One LSP request → the raw <c>result</c> element (null on error/null).</summary>
    Task<JsonElement?> RequestAsync(string method, object? parameters, CancellationToken cancellationToken);
}

/// <summary>
/// Session pool — one LSP server process per workspace, idle-reaped like
/// the browser pool. <see cref="GetAsync"/> returns null when the server is
/// not installed or fails the handshake.
/// </summary>
public interface ILspClientPool
{
    /// <summary>Server binary configured and resolving on PATH — the tool's availability probe.</summary>
    bool IsServerAvailable();

    /// <summary>Get-or-start the session bound to <paramref name="workspacePath"/>.</summary>
    Task<ILspClient?> GetAsync(string workspacePath, CancellationToken cancellationToken = default);
}
