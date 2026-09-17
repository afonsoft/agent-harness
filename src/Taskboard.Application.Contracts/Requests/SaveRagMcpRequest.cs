namespace Taskboard.Requests;

/// <summary>
/// Saves the RAG MCP configuration. <paramref name="ApiKey"/> semantics:
/// <c>null</c> keeps the stored key, <c>""</c> clears it (unauthenticated server).
/// </summary>
public sealed record SaveRagMcpRequest(string? Name, string? Url, string? ApiKey);
