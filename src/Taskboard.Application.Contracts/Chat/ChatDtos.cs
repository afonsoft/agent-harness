namespace Taskboard.Application.Contracts.Chat;

/// <summary>Provider catalog DTO — the API key never leaves the server (RF-001).</summary>
public sealed record ChatProviderDto(
    Guid Id,
    string Name,
    string BaseUrl,
    bool Enabled,
    bool HasApiKey,
    string KeyHint,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ChatProviderUpsertRequest(
    string Name,
    string BaseUrl,
    string? ApiKey = null,
    bool? Enabled = null);

public sealed record ChatModelListDto(IReadOnlyList<string> Models, bool Cached);

/// <summary>
/// Agent-chat binding on a provider-chat conversation
/// (SPEC-20261003-ai-code-agent-chat): the CLI/repo/model picked in the Agent
/// mode bar. The assistant keeps answering; delegated tools (run_agent,
/// run_cli) default to this CLI and run inside this workspace.
/// </summary>
public sealed record ChatAgentContext(
    string? AgentCli = null,
    string? RepositoryFullName = null,
    string? WorkspacePath = null,
    string? AgentModel = null);

public sealed record ChatConversationDto(
    string Id,
    Guid ProviderId,
    string ProviderName,
    string Model,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Preview,
    ChatAgentContext? Agent = null,
    // SPEC-20261005-chat-background-resume: soft-delete flag + live-run badge.
    DateTime? ArchivedAt = null,
    // "waiting-approval" when the active run parks on a pending ChatApproval
    // (SPEC-20261005-chat-tool-approval RF-007).
    string? ActiveRunStatus = null,
    // SPEC-20261005-chat-tool-approval RF-006: chat|ask|full.
    string? PermissionPreset = null,
    // SPEC-20261005-chat-plan-mode RF-001: on|off.
    string? PlanMode = null,
    // SPEC-20261005-chat-fork-steering RF-001/RF-004: fork lineage + count.
    string? ForkedFromConversationId = null,
    string? ForkedAtMessageId = null,
    int ForkCount = 0);

public sealed record ChatMessageDto(
    string Id,
    string Role,
    string Content,
    string? ToolCallsJson,
    string? ToolCallId,
    string? ToolName,
    bool Refused,
    string? ImagePath,
    string? ImageUrl,
    int? TokensIn,
    int? TokensOut,
    string? Model,
    DateTime CreatedAt,
    // SPEC-20261005-chat-context-management RF-004: "normal" | "summary" —
    // the UI labels summaries distinctly (never as user text).
    string Kind = "normal",
    // SPEC-20261005-chat-attachments-feedback RF-004: bound attachments —
    // images render inline, other types as download chips.
    IReadOnlyList<ChatAttachmentDto>? Attachments = null,
    // RF-005: persisted 👍/👎 — assistant messages only.
    ChatFeedbackDto? Feedback = null);

public sealed record ChatConversationDetailDto(
    ChatConversationDto Conversation,
    IReadOnlyList<ChatMessageDto> Messages,
    // SPEC-20261005-chat-background-resume RF-004: live run to attach to +
    // latest finished run (interrupted/failed notice).
    ChatRunDto? ActiveRun = null,
    ChatRunDto? LastRun = null,
    // SPEC-20261005-chat-tool-approval RF-007: unresolved approvals replayed
    // into the pending card — survives a browser-closed run.
    IReadOnlyList<ChatApprovalDto>? PendingApprovals = null,
    // SPEC-20261005-chat-attachments-feedback RF-007: deliverables card of
    // <see cref="LastRun"/> when it completed.
    IReadOnlyList<ChatDeliverableDto>? LastRunDeliverables = null);

/// <summary>
/// A provider-chat run (SPEC-20261005-chat-background-resume RF-004) —
/// returned by the enqueue endpoint, embedded in conversation details and in
/// the attach stream's <c>chat.sync</c>/<c>chat.done</c> payloads.
/// </summary>
public sealed record ChatRunDto(
    string Id,
    string ConversationId,
    string Status,
    string TriggerMessageId,
    string? Error,
    int? TokensIn,
    int? TokensOut,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    // SPEC-20261005-chat-context-management RF-007: compaction stats.
    int? ContextTokensLimit = null,
    int CompactionCount = 0);

/// <summary>
/// The <c>chat.sync</c> payload of the attach stream (RF-003): durable state
/// (persisted messages + in-flight checkpoint) followed by live events whose
/// sequence exceeds <see cref="LastEventSeq"/> — replay is idempotent without
/// a Last-Event-ID cursor.
/// </summary>
public sealed record ChatRunSyncDto(
    IReadOnlyList<ChatMessageDto> Messages,
    ChatRunDto Run,
    string? Partial,
    string? PartialReasoning,
    long LastEventSeq);

public sealed record CreateChatConversationRequest(
    Guid ProviderId,
    string Model,
    string? Title = null,
    ChatAgentContext? Agent = null);

public sealed record SendChatMessageRequest(
    string Content,
    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-005: while a run is active the
    /// message lands in the steer inbox (claimed at the next tool-result
    /// boundary) instead of queueing a new run. No-op when nothing runs.
    /// </summary>
    bool Steer = false,
    /// <summary>
    /// SPEC-20261005-chat-attachments-feedback RF-002: staged attachment ids
    /// bound to this message on send (cap <c>Chat:Attachments:MaxPerMessage</c>).
    /// </summary>
    IReadOnlyList<string>? AttachmentIds = null);

/// <summary>
/// A staged/bound attachment (SPEC-20261005-chat-attachments-feedback RF-001).
/// </summary>
public sealed record ChatAttachmentDto(
    string Id, string FileName, string ContentType, long ByteSize, string DownloadUrl);

/// <summary>
/// Log-only per-message feedback (RF-005/RNF-003) — never on the wire.
/// <see cref="Version"/> is the CAS token of the PUT endpoint.
/// </summary>
public sealed record ChatFeedbackDto(
    string Rating, string? Category, string? Note, long Version, DateTime UpdatedAt);

/// <summary>Body of <c>PUT /api/local/chat/messages/{id}/feedback</c> (RF-005).</summary>
public sealed record PutChatMessageFeedbackRequest(
    string Rating, string? Category = null, string? Note = null, long Version = 0);

/// <summary>
/// One file of a completed run's deliverables card (RF-007) — measured
/// (tool-edit/git) or declared by the model (<c>present</c>).
/// </summary>
public sealed record ChatDeliverableDto(
    string Path, int? AddedLines, int? RemovedLines, string Source, string? Summary = null);

/// <summary>Where a deliverable row came from (RF-007). Wire contract —
/// Integrations and Application both emit these values.</summary>
public static class ChatDeliverableSources
{
    public const string ToolEdit = "tool-edit";
    public const string Git = "git";
    public const string Present = "present";
}

/// <summary>Body of <c>POST /api/local/chat/conversations/{id}/fork</c> (RF-002).</summary>
public sealed record ForkChatConversationRequest(string MessageId);

/// <summary>
/// A pending steer item (SPEC-20261005-chat-fork-steering RF-005) — feeds
/// the withdrawable chip in the composer while unclaimed.
/// </summary>
public sealed record ChatSteerDto(string Id, string Content, DateTime CreatedAt);

/// <summary>SPEC-20261005 RF-009: body of <c>POST /api/local/push/subscriptions</c>
/// — mirrors the browser PushSubscription JSON ({endpoint, keys:{p256dh, auth}}).
/// </summary>
public sealed record PushSubscriptionRequest(
    string Endpoint,
    PushSubscriptionKeysRequest? Keys,
    string? UserAgent = null);

public sealed record PushSubscriptionKeysRequest(string P256dh, string Auth);

/// <summary>
/// 202 body of <c>POST /conversations/{id}/messages</c> — the queued run, or
/// the live run the message steered into (SPEC-20261005-chat-fork-steering).
/// </summary>
public sealed record EnqueueChatMessageResponse(
    ChatRunDto Run, bool Steered = false, string? SteerId = null);

/// <summary>
/// A provider-chat tool approval (SPEC-20261005-chat-tool-approval RF-001) —
/// pending rows feed the approval card; decided rows are the audit trail.
/// </summary>
public sealed record ChatApprovalDto(
    string Id,
    string RunId,
    string ConversationId,
    string ToolCallId,
    string ToolName,
    string ArgumentsPreview,
    string Status,
    DateTime RequestedAt,
    DateTime? DecidedAt,
    string? Decision,
    string? DecidedBy,
    // SPEC-20261005-chat-plan-mode RF-003: tool-call | plan-review.
    string Kind = "tool-call");

/// <summary>Body of <c>POST /api/local/chat/approvals/{id}/decide</c> (RF-003/RF-004).</summary>
public sealed record DecideChatApprovalRequest(
    /// <summary>"allow" → allowed-once; "deny" → rejected.</summary>
    string Outcome,
    string? Reason = null,
    /// <summary>RF-004: add the tool to the conversation allowed-list (allow only).</summary>
    bool RememberTool = false);

/// <summary>Body of <c>POST /api/local/chat/conversations/{id}/plan-mode</c> (RF-001).</summary>
public sealed record SetChatPlanModeRequest(bool Active);

public sealed record PatchChatConversationRequest(
    string? Title = null,
    string? Model = null,
    ChatAgentContext? Agent = null,
    // SPEC-20261005-chat-tool-approval RF-006: chat|ask|full — editable mid-run.
    string? PermissionPreset = null);
