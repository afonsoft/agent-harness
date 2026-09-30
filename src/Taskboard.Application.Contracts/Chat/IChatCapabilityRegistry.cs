namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Unified catalog of invocable chat capabilities and the effective tool set
/// for a conversation (SPEC-20261001-chat-capability-registry). Enablement is
/// governed by master switches per kind plus the per-capability
/// <c>Taskboard:Chat:Capabilities:Disabled</c> JSON list.
/// </summary>
public interface IChatCapabilityRegistry
{
    /// <summary>
    /// Lists every discovered capability — builtin tools, MCP tools, global
    /// skills and agent delegation — with its effective enabled state.
    /// </summary>
    Task<IReadOnlyList<ChatCapability>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the <see cref="IChatTool"/> set to send to the provider for the
    /// current configuration — disabled capabilities are absent, so the model
    /// cannot invoke them.
    /// </summary>
    Task<IReadOnlyDictionary<string, IChatTool>> ResolveToolSetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Effective enabled state of one capability id (master switch AND not in
    /// the disabled list). Used by meta-tools like <c>use_skill</c> that gate
    /// per-item capability ids at call time.
    /// </summary>
    bool IsCapabilityEnabled(string capabilityId);
}
