namespace Taskboard.Application.Contracts.Mcp;

/// <summary>
/// Result of <c>POST /api/mcp/rag/test</c> (SPEC-20261003-ops-hardening RF-003):
/// a live MCP handshake against the configured RAG server. Never carries secrets.
/// </summary>
public sealed record RagTestResult(bool Ok, long? LatencyMs, int? Tools, string? Error);
