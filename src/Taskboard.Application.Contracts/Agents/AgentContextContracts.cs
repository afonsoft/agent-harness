namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261104-agent-context-templates RF-001/RF-004: claims carried by an
/// <c>agent-callback</c> token — scoped to one scope + task (+ dispatch when
/// known). Verified without a DB hit (HMAC-signed claims).
/// </summary>
public sealed record AgentCallbackClaims(
    string SessionKey,
    string Scope,
    string? TaskId,
    string? DispatchId,
    string AgentName,
    /// <summary>Capabilities: <c>mailbox</c> (write own scope) and/or <c>status</c>.</summary>
    IReadOnlyList<string> Capabilities,
    DateTime ExpiresAtUtc);

/// <summary>
/// Mints and validates <c>agent-callback</c> tokens injected as
/// <c>HARNESS_AGENT_TOKEN</c>. Tokens are HMAC-signed — no store lookup on
/// validation; TTL covers the run lifetime.
/// </summary>
public interface IAgentCallbackTokenService
{
    /// <summary>Mints a token for the given claims (expiry supplied by the service).</summary>
    string Mint(
        string sessionKey, string scope, string? taskId, string? dispatchId,
        string agentName, IReadOnlyList<string> capabilities);

    /// <summary>Returns the claims when the signature and expiry verify; null otherwise.</summary>
    AgentCallbackClaims? Validate(string token);
}

/// <summary>Capabilities granted to callback tokens.</summary>
public static class AgentCallbackCapabilities
{
    /// <summary>Mailbox write into the token's own scope (send/ask/done/escalation).</summary>
    public const string Mailbox = "mailbox";

    /// <summary>Explicit status report into the attention store.</summary>
    public const string Status = "status";
}

/// <summary>
/// SPEC-20261104-agent-context-templates RF-003: a named task template —
/// frontmatter over a Markdown body stored in the local template dir.
/// </summary>
public sealed record AgentTemplateDto(
    string Name,
    string? Description,
    string? Cli,
    string? Command,
    string? Model,
    string? Effort,
    bool? Interactive,
    string Body,
    /// <summary>Validation error marker — catalog keeps listing on bad files.</summary>
    string? Error);

/// <summary>Wire shape for <c>POST /api/local/agent/send</c>.</summary>
public sealed record AgentSendRequest(string Text);

/// <summary>Wire shape for <c>POST /api/local/agent/status</c>.</summary>
public sealed record AgentStatusRequest(string State, string? Note = null);

/// <summary>Wire shape for <c>POST /api/local/agent/done</c>.</summary>
public sealed record AgentDoneRequest(string Summary);

/// <summary>Wire shape for <c>POST /api/local/agent/ask</c>.</summary>
public sealed record AgentAskRequest(
    string Question,
    IReadOnlyList<string>? Options = null,
    int? TimeoutSeconds = null);

/// <summary>Catalog over <c>{dataDir}/templates/*.md</c>.</summary>
public interface IAgentTemplateCatalog
{
    /// <summary>All templates, including invalid ones (marked with <see cref="AgentTemplateDto.Error"/>).</summary>
    Task<IReadOnlyList<AgentTemplateDto>> ListAsync(CancellationToken ct = default);

    /// <summary>One template by name; null when absent or invalid.</summary>
    Task<AgentTemplateDto?> GetAsync(string name, CancellationToken ct = default);
}
