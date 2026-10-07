using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>Streamed event of a chat turn (RF-005) — written as SSE by the endpoint.</summary>
public abstract record ChatStreamEvent;

public sealed record ChatDeltaEvent(string Content) : ChatStreamEvent;

public sealed record ChatToolCallEvent(string Name, string ArgumentsJson) : ChatStreamEvent;

public sealed record ChatToolResultEvent(string Name, string ResultJson, bool Refused, string? RefusalReason) : ChatStreamEvent;

/// <summary>
/// Live activity status (SPEC-20261001-chat-ux-compact FR-003): emitted while a
/// tool/MCP/agent/sub-agent runs so the UI can show "Running X…" chips.
/// </summary>
public sealed record ChatStatusEvent(string Phase, string? Label) : ChatStreamEvent;

/// <summary>Reasoning-model thinking delta (delta.reasoning_content) —
/// streamed for live UI feedback, never persisted nor re-sent to the model.</summary>
public sealed record ChatReasoningEvent(string Content) : ChatStreamEvent;

public sealed record ChatDoneEvent(int? TokensIn, int? TokensOut, string? FinishReason, string? Error = null) : ChatStreamEvent;

/// <summary>
/// Internal plumbing (SPEC-20261005-chat-background-resume): marks the point
/// where the in-flight assistant text became a durable ChatMessage — the
/// broadcaster clears its partial checkpoint here. Never serialized to SSE.
/// </summary>
public sealed record ChatPersistedEvent : ChatStreamEvent;

/// <summary>
/// SPEC-20261005-chat-tool-approval RF-002: the loop parked a mutating tool
/// call on a persisted <see cref="ChatApproval"/> — SSE <c>approval.asked</c>.
/// </summary>
public sealed record ChatApprovalAskedEvent(
    string ApprovalId, string ToolCallId, string ToolName, string ArgumentsPreview,
    string Kind = "tool-call",
    // SPEC-20261013-chat-risk-approvals RF-004: the `auto` classifier's
    // verdict a high-risk card carries (null on policy asks).
    string? Risk = null, string? RiskReason = null) : ChatStreamEvent;

/// <summary>RF-003/RF-005: the pending approval resolved — SSE <c>approval.decided</c>.</summary>
public sealed record ChatApprovalDecidedEvent(
    string ApprovalId, string Status, string? Decision, string DecidedBy,
    string Kind = "tool-call") : ChatStreamEvent;

/// <summary>
/// SPEC-20261005-chat-context-management RF-007: wire-token estimate + the
/// effective budget, emitted after each pre-call estimation — SSE
/// <c>chat.pressure</c> + <c>run.pressure</c> hub event (header meter, sidebar
/// badge).
/// </summary>
public sealed record ChatPressureEvent(
    int EstimatedTokens, int Limit, bool Compacted) : ChatStreamEvent;

/// <summary>
/// SPEC-20261005-chat-fork-steering RF-006: the executor drained a pending
/// steer at a tool-result boundary — SSE <c>steer.claimed</c> so attached
/// clients persist a transcript refresh.
/// </summary>
public sealed record ChatSteerClaimedEvent(string SteerId, string Content) : ChatStreamEvent;

/// <summary>
/// SPEC-20261005-chat-attachments-feedback RF-007: the run's changed/declared
/// files — SSE <c>chat.deliverables</c>; the same rows persist as
/// <c>ChatRunDeliverable</c> for the post-reload card.
/// </summary>
public sealed record ChatDeliverablesEvent(IReadOnlyList<ChatDeliverableDto> Deliverables) : ChatStreamEvent;

/// <summary>SPEC-20261012-chat-run-controls: the run parked at a boundary — SSE <c>chat.paused</c>.</summary>
public sealed record ChatPausedEvent : ChatStreamEvent;

/// <summary>SPEC-20261012-chat-run-controls: a parked run is running again — SSE <c>chat.resumed</c>.</summary>
public sealed record ChatResumedEvent : ChatStreamEvent;

/// <summary>
/// SPEC-20261013-chat-risk-approvals RF-004: an <c>auto</c>-policy call
/// classified <c>medium</c> ran without a prompt — SSE <c>risk.notice</c>,
/// rendered as a badge on the tool card.
/// </summary>
public sealed record ChatRiskNoticeEvent(
    string ToolCallId, string ToolName, string Risk, string Reason) : ChatStreamEvent;

/// <summary>
/// Provider chat orchestration (SPEC-20260929-ai-code-provider-chat): provider
/// CRUD, conversation persistence with search, and the send/stream tool loop
/// (RF-005) over the OpenAI-compatible client with auto-confined tools (RF-006).
/// </summary>
public sealed class ChatService(
    IRepository<ChatProvider> providers,
    IRepository<ChatConversation> conversations,
    IRepository<ChatMessage> messages,
    OpenAiCompatibleClient client,
    IChatCapabilityRegistry capabilities,
    Taskboard.Application.Contracts.Skills.ISkillDiscoveryService skills,
    IWorkspacePathResolver workspace,
    IConfiguration configuration,
    ChatRunCoordinator runs,
    HybridCache cache,
    IRepository<ChatRun> runRepository,
    ChatRunQueue runQueue,
    ChatRunBroadcaster broadcaster,
    IRepository<ChatApproval> approvalRepository,
    IRepository<ChatSteer> steerRepository,
    ChatApprovalCoordinator approvalCoordinator,
    IEnumerable<IChatRunNotifier> notifiers,
    ILogger<ChatService> logger,
    TimeProvider? clock = null,
    ISpillStore? spillStore = null,
    IRepository<ChatAttachment>? attachmentRepository = null,
    IRepository<ChatMessageFeedback>? feedbackRepository = null,
    IRepository<ChatRunDeliverable>? deliverableRepository = null,
    ChatAttachmentStore? attachmentStore = null,
    IChatFileEditTracker? editTracker = null,
    IChatWorkspaceDiffService? workspaceDiff = null,
    IChatMessageSearchIndex? searchIndex = null,
    IChatToolRiskClassifier? riskClassifier = null) : IChatRunExecutor
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IChatToolRiskClassifier _riskClassifier =
        riskClassifier ?? StaticChatToolRiskClassifier.Instance;

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    // SPEC-20261004-provider-pick-hybridcache RF-002: chat catalog caching via
    // HybridCache (L1 memory; L2 = Redis when Taskboard:Cache:Redis is set).
    internal const string ProvidersCacheKey = "chat-providers";
    internal const string ProvidersCacheTag = "chat-providers";
    internal const string ProviderModelsCacheTag = "chat-provider-models";
    internal static readonly TimeSpan ProvidersTtl = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ProviderModelsTtl = TimeSpan.FromMinutes(5);

    private static string ProviderModelsKey(Guid providerId) => $"chat-provider-models-{providerId}";
    private static string ProviderKey(Guid providerId) => $"chat-provider-{providerId}";

    // ---- Providers (RF-001/RF-002) ----

    public async Task<IReadOnlyList<ChatProviderDto>> ListProvidersAsync(CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ProvidersCacheKey,
            async inner =>
            {
                var rows = await providers.Query.OrderBy(p => p.Name).ToListAsync(inner).ConfigureAwait(false);
                return (IReadOnlyList<ChatProviderDto>)rows.Select(ToDto).ToList();
            },
            new HybridCacheEntryOptions { Expiration = ProvidersTtl },
            [ProvidersCacheTag],
            ct).ConfigureAwait(false);
    }

    public async Task<ChatProviderDto> CreateProviderAsync(ChatProviderUpsertRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            throw new ChatValidationException("Name and base URL are required.");
        }

        if (await providers.Query.AnyAsync(p => p.Name == name, ct).ConfigureAwait(false))
        {
            throw new ChatValidationException("A provider with this name already exists.");
        }

        var provider = ChatProvider.Create(name, request.BaseUrl, request.ApiKey ?? string.Empty, UtcNow);
        await providers.AddAsync(provider, ct).ConfigureAwait(false);
        await providers.SaveChangesAsync(ct).ConfigureAwait(false);
        await cache.RemoveByTagAsync(ProvidersCacheTag, ct).ConfigureAwait(false);
        return ToDto(provider);
    }

    public async Task<ChatProviderDto?> UpdateProviderAsync(Guid id, ChatProviderUpsertRequest request, CancellationToken ct = default)
    {
        var provider = await providers.GetAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return null;
        }

        var name = request.Name.Trim();
        if (await providers.Query.AnyAsync(p => p.Name == name && p.Id != id, ct).ConfigureAwait(false))
        {
            throw new ChatValidationException("A provider with this name already exists.");
        }

        provider.Update(request.Name, request.BaseUrl, request.ApiKey, request.Enabled, UtcNow);
        await providers.SaveChangesAsync(ct).ConfigureAwait(false);
        await InvalidateProviderCachesAsync(provider.Id, ct).ConfigureAwait(false);
        return ToDto(provider);
    }

    public async Task<bool> DeleteProviderAsync(Guid id, CancellationToken ct = default)
    {
        var provider = await providers.GetAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return false;
        }

        await providers.DeleteAsync(provider, ct).ConfigureAwait(false);
        await providers.SaveChangesAsync(ct).ConfigureAwait(false);
        await InvalidateProviderCachesAsync(id, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<ChatModelListDto> ListModelsAsync(Guid providerId, CancellationToken ct = default)
    {
        var provider = await RequireProviderAsync(providerId, ct).ConfigureAwait(false);
        try
        {
            var models = await cache.GetOrCreateAsync(
                ProviderModelsKey(providerId),
                async inner => await client.ListModelsAsync(provider.BaseUrl, provider.ApiKey, inner).ConfigureAwait(false),
                new HybridCacheEntryOptions { Expiration = ProviderModelsTtl },
                [ProviderModelsCacheTag, ProvidersCacheTag],
                ct).ConfigureAwait(false);
            return new ChatModelListDto(models, Cached: true);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatProviderException($"Provider unreachable: {ex.Message}", 502);
        }
    }

    private async Task InvalidateProviderCachesAsync(Guid providerId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(ProvidersCacheTag, ct).ConfigureAwait(false);
        await cache.RemoveAsync(ProviderModelsKey(providerId), ct).ConfigureAwait(false);
        await cache.RemoveAsync(ProviderKey(providerId), ct).ConfigureAwait(false);
    }

    // ---- Conversations (RF-004) ----

    public async Task<ChatConversationDto> CreateConversationAsync(
        CreateChatConversationRequest request, CancellationToken ct = default)
    {
        var provider = await RequireProviderAsync(request.ProviderId, ct).ConfigureAwait(false);
        if (!provider.Enabled)
        {
            throw new ChatValidationException($"Provider '{provider.Name}' is disabled.");
        }

        var conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), provider.Id, provider.Name, request.Model, request.Title, UtcNow);
        // RF-006: new conversations inherit the global default preset
        // (Taskboard:Chat:Approval:Preset — "ask" when unset/invalid).
        conversation.SetPermissionPreset(ChatApprovalPolicy.DefaultPreset(configuration), UtcNow);
        // RF-001/P2: Taskboard:Chat:PlanMode:Default starts new
        // conversations in plan mode when enabled (off when unset).
        conversation.SetPlanMode(ChatPlanMode.DefaultOn(configuration), UtcNow);
        if (request.Agent is not null)
        {
            // SPEC-20261004 RF-002: the workspace path is confined to $HOME —
            // anything else normalizes to null (default ~/repos resolution).
            conversation.SetAgentContext(
                request.Agent.AgentCli, request.Agent.RepositoryFullName,
                workspace.NormalizeWorkspacePath(request.Agent.WorkspacePath), request.Agent.AgentModel);
        }

        await conversations.AddAsync(conversation, ct).ConfigureAwait(false);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    public async Task<IReadOnlyList<ChatConversationDto>> ListConversationsAsync(
        string? query, bool archived = false, CancellationToken ct = default,
        bool hasNegativeFeedback = false)
    {
        // RF-006: archive is a view flag — active/archived lists are disjoint.
        var rows = await conversations.Query
            .Where(c => (c.ArchivedAt != null) == archived)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(200)
            .ToListAsync(ct).ConfigureAwait(false);

        // SPEC-20261005-chat-attachments-feedback RF-005: the "needs review"
        // sidebar filter — conversations carrying at least one 👎.
        if (hasNegativeFeedback && feedbackRepository is not null)
        {
            var negativeIds = (await feedbackRepository.Query
                    .AsNoTracking()
                    .Where(f => f.Rating == ChatFeedbackRatings.Negative)
                    .Select(f => f.ConversationId)
                    .Distinct()
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(i => i.Value)
                .ToHashSet(StringComparer.Ordinal);
            rows = rows.Where(c => negativeIds.Contains(c.Id.Value)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var matchingIds = await messages.Query
                .Where(m => m.Role == ChatMessageRole.User && m.Content.Contains(term))
                .Select(m => m.ConversationId)
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false);
            var idSet = matchingIds.Select(i => i.Value).ToHashSet(StringComparer.Ordinal);
            rows = rows.Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || idSet.Contains(c.Id.Value)).ToList();
        }

        var previews = await messages.Query
            .Where(m => m.Role == ChatMessageRole.User)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var previewByConversation = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in previews)
        {
            var key = message.ConversationId.Value;
            if (!previewByConversation.ContainsKey(key))
            {
                previewByConversation[key] = message.Content.Length <= 120 ? message.Content : $"{message.Content[..120]}…";
            }
        }

        // Live-run badge (RF-007): a run survives the browser — the chip is
        // the only signal that work is still going.
        var activeRuns = await runRepository.Query
            .Where(r => r.Status == ChatRunStatus.Queued || r.Status == ChatRunStatus.Running)
            .ToListAsync(ct).ConfigureAwait(false);
        var activeByConversation = activeRuns
            .GroupBy(r => r.ConversationId.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Any(r => r.Status == ChatRunStatus.Running)
                    ? ChatRunStatus.Running.Value
                    : ChatRunStatus.Queued.Value,
                StringComparer.Ordinal);

        // SPEC-20261005-chat-tool-approval RF-007: a run parked on a pending
        // approval gets a distinct badge — it is stuck until a human decides.
        if (activeRuns.Count > 0)
        {
            var activeRunIds = activeRuns.Select(r => r.Id.Value).ToHashSet(StringComparer.Ordinal);
            var pendingApprovals = await approvalRepository.Query
                .Where(a => a.Status == ChatApprovalStatus.Pending)
                .Select(a => new { a.RunId, a.ConversationId })
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var approval in pendingApprovals.Where(a => activeRunIds.Contains(a.RunId.Value)))
            {
                activeByConversation[approval.ConversationId.Value] = "waiting-approval";
            }
        }

        // SPEC-20261005-chat-fork-steering RF-004: fork badge on the source row.
        var forkCountBySource = (await conversations.Query
                .Where(c => c.ForkedFromConversationId != null)
                .GroupBy(c => c.ForkedFromConversationId!)
                .Select(g => new { Source = g.Key, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Source, x => x.Count, StringComparer.Ordinal);

        return rows.Select(c => ToDto(
            c,
            previewByConversation.GetValueOrDefault(c.Id.Value),
            activeByConversation.GetValueOrDefault(c.Id.Value),
            forkCountBySource.GetValueOrDefault(c.Id.Value))).ToList();
    }

    public async Task<ChatConversationDetailDto?> GetConversationAsync(string id, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var rows = await messages.Query
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        // RF-004: the running/earliest-queued run is the attach target; the
        // latest finished run feeds the "interrupted" inline notice.
        var runRows = await runRepository.Query
            .Where(r => r.ConversationId == conversation.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync(ct).ConfigureAwait(false);
        var active = runRows
            .Where(r => r.Status.IsActive)
            .OrderByDescending(r => r.Status == ChatRunStatus.Running)
            .ThenBy(r => r.CreatedAt)
            .FirstOrDefault();
        var last = runRows.FirstOrDefault(r => r.Status.IsTerminal);

        // RF-007: pending approvals replay into the card — live SSE is not
        // required (a browser-closed run still surfaces the question).
        var pendingApprovals = await approvalRepository.Query
            .Where(a => a.ConversationId == conversation.Id && a.Status == ChatApprovalStatus.Pending)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        // RF-007: the completed run's deliverables card replays on load.
        IReadOnlyList<ChatDeliverableDto>? lastRunDeliverables = null;
        if (last is not null && deliverableRepository is not null)
        {
            var deliverableRows = await deliverableRepository.Query
                .AsNoTracking()
                .Where(d => d.RunId == last.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            lastRunDeliverables = deliverableRows.Select(ToDto).ToList();
        }

        return new ChatConversationDetailDto(
            ToDto(conversation, null), await EnrichMessagesAsync(rows, ct).ConfigureAwait(false),
            active is null ? null : ToDto(active),
            last is null ? null : ToDto(last),
            pendingApprovals.Select(ToDto).ToList(),
            lastRunDeliverables);
    }

    public async Task<ChatConversationDto?> PatchConversationAsync(
        string id, PatchChatConversationRequest request, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            conversation.Rename(request.Title, UtcNow);
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            conversation.ChangeModel(request.Model, UtcNow);
        }

        if (request.Agent is not null)
        {
            // SPEC-20261004 RF-002: clamp — same rule as create.
            conversation.SetAgentContext(
                request.Agent.AgentCli, request.Agent.RepositoryFullName,
                workspace.NormalizeWorkspacePath(request.Agent.WorkspacePath), request.Agent.AgentModel);
        }

        // SPEC-20261005-chat-tool-approval RF-006: preset editable mid-run —
        // takes effect on the next tool call; switching clears the
        // allowed-list and leaves an audit note in the transcript.
        if (!string.IsNullOrWhiteSpace(request.PermissionPreset))
        {
            var previous = conversation.PermissionPreset;
            conversation.SetPermissionPreset(request.PermissionPreset, UtcNow);
            if (conversation.PermissionPreset != previous)
            {
                var presetNote = ChatMessage.CreateSystemNote(
                    conversation.Id,
                    $"Permission preset changed: {previous} → {conversation.PermissionPreset}.",
                    UtcNow);
                await messages.AddAsync(presetNote, ct).ConfigureAwait(false);
                await IndexMessageSafeAsync(presetNote, ct).ConfigureAwait(false);
            }
        }

        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    /// <summary>RF-006: archive/unarchive is a view flag — the run keeps going in background.</summary>
    public async Task<ChatConversationDto?> SetConversationArchivedAsync(
        string id, bool archived, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        if (archived)
        {
            conversation.Archive(UtcNow);
        }
        else
        {
            conversation.Unarchive(UtcNow);
        }

        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    /// <summary>Latest run row for the conversation/id pair — the <c>chat.done</c> payload (RF-003).</summary>
    public async Task<ChatRunDto?> GetRunAsync(
        string conversationId, string runId, CancellationToken ct = default)
    {
        var run = await runRepository.GetAsync(ChatRunId.From(runId), ct).ConfigureAwait(false);
        return run is null || run.ConversationId.Value != conversationId ? null : ToDto(run);
    }

    public async Task<bool> DeleteConversationAsync(string id, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return false;
        }

        // Hard delete kills any live run first — the cascade would drop the
        // rows, but the executor must release its token.
        await StopAsync(id, ct).ConfigureAwait(false);
        await conversations.DeleteAsync(conversation, ct).ConfigureAwait(false);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ---- Enqueue / execute / stop (SPEC-20261005-chat-background-resume RF-002..RF-004) ----

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-005: queues <paramref name="content"/>
    /// into the live run's steer inbox — claimed by the executor at the next
    /// tool-result boundary (RF-006). Returns <c>null</c> when no run is
    /// running so the caller degrades to <see cref="EnqueueMessageAsync"/>.
    /// 409 when the inbox is full (<see cref="ChatSteer.MaxPendingPerRun"/>).
    /// </summary>
    public async Task<(ChatRunDto Run, string SteerId)?> EnqueueSteerAsync(
        string conversationId, string content, CancellationToken ct = default,
        IReadOnlyList<string>? attachmentIds = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ChatValidationException("Content is required.");
        }

        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        if (conversation.ArchivedAt is not null)
        {
            throw new ChatArchivedException($"Conversation '{conversationId}' is archived.");
        }

        var active = await runRepository.Query
            .Where(r => r.ConversationId == conversation.Id && r.Status == ChatRunStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (active is null)
        {
            return null;
        }

        var pending = await steerRepository.Query
            .CountAsync(s => s.RunId == active.Id && s.ClaimedAt == null, ct)
            .ConfigureAwait(false);
        if (pending >= ChatSteer.MaxPendingPerRun)
        {
            throw new ChatSteerConflictException(
                $"Steer inbox is full ({ChatSteer.MaxPendingPerRun} pending).");
        }

        // Attachments ride the steer — the drain binds them to the steer
        // message it persists and appends descriptors to the wire text.
        var item = ChatSteer.Create(
            ChatSteerId.NewGuid(), active.Id, conversation.Id, content.Trim(), UtcNow,
            attachmentIds is { Count: > 0 } ? JsonSerializer.Serialize(attachmentIds) : null);
        await steerRepository.AddAsync(item, ct).ConfigureAwait(false);
        conversation.Touch(UtcNow);
        await steerRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        await NotifySteerQueuedAsync(conversation.Id.Value, item.Id.Value, item.Content, ct)
            .ConfigureAwait(false);
        return (ToDto(active), item.Id.Value);
    }

    /// <summary>
    /// SPEC-20261015-chat-preview-panel RF-002: pins the preview tab target.
    /// The input is normalized through <see cref="ChatPreviewUrl.Normalize"/>
    /// — loopback http(s) URLs become <c>/preview/{port}/{path}</c>, anything
    /// else is a 400-class validation error.
    /// </summary>
    public async Task<ChatConversationDto?> SetConversationPreviewAsync(
        string id, string url, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var normalized = ChatPreviewUrl.Normalize(url)
            ?? throw new ChatValidationException(
                $"Invalid preview URL '{url}' — loopback http(s) or /preview/… only.");
        conversation.SetPreviewUrl(normalized, UtcNow);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    /// <summary>RF-002: clears the pinned preview target.</summary>
    public async Task<ChatConversationDto?> ClearConversationPreviewAsync(
        string id, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        conversation.SetPreviewUrl(null, UtcNow);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    public async Task<ChatRunDto> EnqueueMessageAsync(
        string conversationId, string content, CancellationToken ct = default,
        IReadOnlyList<string>? attachmentIds = null,
        ChatElementQuote? quote = null)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        if (conversation.ArchivedAt is not null)
        {
            throw new ChatArchivedException($"Conversation '{conversationId}' is archived.");
        }

        if (await GetProviderSnapshotAsync(conversation.ProviderId, ct).ConfigureAwait(false) is null)
        {
            throw new ChatValidationException($"Provider '{conversation.ProviderName}' no longer exists.");
        }

        // SPEC-20261015-chat-preview-panel RF-004: the picked element rides
        // the user message as a markdown quote card — persisted, rendered
        // in the transcript and visible to the model as context.
        var titleSeed = content;
        if (quote is not null)
        {
            var text = quote.Text is { Length: > 0 } t ? $" \"{t}\"" : string.Empty;
            var page = quote.PageUrl is { Length: > 0 } p ? $" — {p}" : string.Empty;
            content = $"> `{quote.Selector}`{text}{page}\n\n{content}";
        }

        var userMessage = ChatMessage.CreateUser(conversation.Id, content, UtcNow);
        await messages.AddAsync(userMessage, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(userMessage, ct).ConfigureAwait(false);
        conversation.EnsureTitle(titleSeed, UtcNow);
        conversation.Touch(UtcNow);

        // SPEC-20261005-chat-attachments-feedback RF-002: bind the staged
        // uploads to this message (cap + ownership enforced).
        await BindAttachmentsAsync(conversation.Id, userMessage.Id, attachmentIds, ct).ConfigureAwait(false);

        var run = ChatRun.Create(ChatRunId.NewGuid(), conversation.Id, userMessage.Id, UtcNow);
        await runRepository.AddAsync(run, ct).ConfigureAwait(false);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);

        runQueue.Enqueue(new ChatRunWorkItem(run.Id.Value, conversation.Id.Value));
        return ToDto(run);
    }

    /// <summary>
    /// SPEC-20261005-chat-jobs-schedule-search RF-006: a ChatSchedule fired —
    /// same enqueue path as <see cref="EnqueueMessageAsync"/> but the user
    /// message carries <c>Kind="schedule"</c> for the transcript badge. The
    /// wire transcript sees a plain user turn.
    /// </summary>
    public async Task<ChatRunDto> EnqueueScheduledMessageAsync(
        string conversationId, string content, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        if (conversation.ArchivedAt is not null)
        {
            throw new ChatArchivedException($"Conversation '{conversationId}' is archived.");
        }

        if (await GetProviderSnapshotAsync(conversation.ProviderId, ct).ConfigureAwait(false) is null)
        {
            throw new ChatValidationException($"Provider '{conversation.ProviderName}' no longer exists.");
        }

        var userMessage = ChatMessage.CreateScheduled(conversation.Id, content, UtcNow);
        await messages.AddAsync(userMessage, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(userMessage, ct).ConfigureAwait(false);
        conversation.EnsureTitle(content, UtcNow);
        conversation.Touch(UtcNow);

        var run = ChatRun.Create(ChatRunId.NewGuid(), conversation.Id, userMessage.Id, UtcNow);
        await runRepository.AddAsync(run, ct).ConfigureAwait(false);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);

        runQueue.Enqueue(new ChatRunWorkItem(run.Id.Value, conversation.Id.Value));
        return ToDto(run);
    }

    /// <summary>
    /// SPEC-20261005-chat-jobs-schedule-search: a durable system note —
    /// background-job completion and schedule-delivery markers. Also the
    /// single chokepoint for FTS sync of notes.
    /// </summary>
    public async Task PostSystemNoteAsync(
        string conversationId, string content, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        var note = ChatMessage.CreateSystemNote(conversation.Id, content, UtcNow);
        await messages.AddAsync(note, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(note, ct).ConfigureAwait(false);
        conversation.Touch(UtcNow);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20261005-chat-jobs-schedule-search RF-007: FTS sync on insert —
    /// failures degrade search coverage, never the message write itself.
    /// </summary>
    private async Task IndexMessageSafeAsync(ChatMessage message, CancellationToken ct)
    {
        if (searchIndex is null)
        {
            return;
        }

        try
        {
            await searchIndex.IndexAsync(
                message.Id.Value, message.ConversationId.Value, message.Content, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FTS index update failed for message {MessageId}", message.Id.Value);
        }
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-007: withdraws an unclaimed steer —
    /// 404 when unknown, 409 once the executor claimed it.
    /// </summary>
    public async Task CancelSteerAsync(string conversationId, string steerId, CancellationToken ct = default)
    {
        var steer = await steerRepository.GetAsync(ChatSteerId.From(steerId), ct).ConfigureAwait(false);
        if (steer is null || !string.Equals(steer.ConversationId.Value, conversationId, StringComparison.Ordinal))
        {
            throw new ChatValidationException($"Steer '{steerId}' not found.");
        }

        if (steer.ClaimedAt is not null)
        {
            throw new ChatSteerConflictException($"Steer '{steerId}' was already claimed.");
        }

        await steerRepository.DeleteAsync(steer, ct).ConfigureAwait(false);
        await steerRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        await NotifySteerResolvedAsync(conversationId, steerId, "cancelled", ct).ConfigureAwait(false);
    }

    // ---- SPEC-20261005-chat-attachments-feedback: attachments ----

    private const string AttachmentsEnabledKey = "Taskboard:Chat:Attachments:Enabled";
    private const string AttachmentsMaxBytesKey = "Taskboard:Chat:Attachments:MaxBytes";
    private const string AttachmentsMaxPerMessageKey = "Taskboard:Chat:Attachments:MaxPerMessage";
    private const string AttachmentsAllowedMimeKey = "Taskboard:Chat:Attachments:AllowedMime";
    private const long DefaultAttachmentsMaxBytes = 8L * 1024 * 1024;
    private const int DefaultAttachmentsMaxPerMessage = 5;

    private bool AttachmentsEnabled =>
        !string.Equals(configuration[AttachmentsEnabledKey], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Allowlist entries — comma list, supports <c>prefix/*</c>.</summary>
    private IReadOnlyList<string> AllowedAttachmentMime()
    {
        var raw = configuration[AttachmentsAllowedMimeKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ChatAttachmentSniffer.DefaultAllowedMime;
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// RF-001/RF-008: stages an upload — MIME comes from sniffed bytes, never
    /// the declared header (RNF-002); over-cap/ allowlist reject as 400.
    /// </summary>
    public async Task<ChatAttachmentDto> UploadAttachmentAsync(
        string conversationId, string fileName, byte[] bytes, CancellationToken ct = default)
    {
        if (attachmentRepository is null || attachmentStore is null)
        {
            throw new ChatValidationException("Attachments are not available.");
        }

        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        if (conversation.ArchivedAt is not null)
        {
            throw new ChatArchivedException($"Conversation '{conversationId}' is archived.");
        }

        if (!AttachmentsEnabled)
        {
            throw new ChatValidationException("Attachments are disabled (Taskboard:Chat:Attachments:Enabled).");
        }

        var maxBytes = ParseLong(AttachmentsMaxBytesKey, DefaultAttachmentsMaxBytes);
        if (bytes.LongLength > maxBytes)
        {
            throw new ChatValidationException($"Attachment exceeds the {maxBytes} byte limit.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "attachment";
        }

        var sniffed = ChatAttachmentSniffer.Sniff(
            bytes.AsSpan(0, Math.Min(bytes.Length, 8192)), fileName, AllowedAttachmentMime());
        if (sniffed is null)
        {
            throw new ChatValidationException($"Attachment type is not allowed for '{fileName}'.");
        }

        var id = ChatAttachmentId.NewGuid();
        var (storagePath, sha256) = attachmentStore.Save(id.Value, sniffed, bytes);
        var row = ChatAttachment.Create(
            id, conversation.Id, fileName.Trim(), sniffed, bytes.LongLength, storagePath, sha256, UtcNow);
        await attachmentRepository.AddAsync(row, ct).ConfigureAwait(false);
        await attachmentRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>RF-001: resolves a bound/staged attachment for download (row + file).</summary>
    public async Task<(ChatAttachment Attachment, string FullPath)?> GetAttachmentAsync(
        string conversationId, string attachmentId, CancellationToken ct = default)
    {
        if (attachmentRepository is null || attachmentStore is null)
        {
            return null;
        }

        var row = await attachmentRepository.GetAsync(ChatAttachmentId.From(attachmentId), ct).ConfigureAwait(false);
        if (row is null || !string.Equals(row.ConversationId.Value, conversationId, StringComparison.Ordinal))
        {
            return null;
        }

        var full = attachmentStore.ResolvePath(row.StoragePath);
        return full is null ? null : (row, full);
    }

    /// <summary>RF-001: delete allowed only while staged — bound rows are transcript history.</summary>
    public async Task DeleteAttachmentAsync(
        string conversationId, string attachmentId, CancellationToken ct = default)
    {
        if (attachmentRepository is null)
        {
            throw new ChatValidationException("Attachments are not available.");
        }

        var row = await attachmentRepository.GetAsync(ChatAttachmentId.From(attachmentId), ct).ConfigureAwait(false);
        if (row is null || !string.Equals(row.ConversationId.Value, conversationId, StringComparison.Ordinal))
        {
            throw new ChatValidationException($"Attachment '{attachmentId}' not found.");
        }

        if (row.MessageId is not null)
        {
            throw new ChatConflictException($"Attachment '{attachmentId}' is already bound to a message.");
        }

        attachmentStore?.Delete(row.StoragePath);
        await attachmentRepository.DeleteAsync(row, ct).ConfigureAwait(false);
        await attachmentRepository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>RF-001: orphan sweep — staged rows older than <paramref name="maxAge"/>.</summary>
    public async Task<int> SweepOrphanedAttachmentsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        if (attachmentRepository is null)
        {
            return 0;
        }

        var cutoff = UtcNow - maxAge;
        var orphans = await attachmentRepository.Query
            .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
            .Take(500)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var orphan in orphans)
        {
            attachmentStore?.Delete(orphan.StoragePath);
            await attachmentRepository.DeleteAsync(orphan, ct).ConfigureAwait(false);
        }

        if (orphans.Count > 0)
        {
            await attachmentRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return orphans.Count;
    }

    /// <summary>
    /// RF-002: validates + binds the staged attachments to a just-created
    /// message — same conversation, staged, under the cap.
    /// </summary>
    private async Task BindAttachmentsAsync(
        ChatConversationId conversationId, ChatMessageId messageId,
        IReadOnlyList<string>? attachmentIds, CancellationToken ct)
    {
        if (attachmentIds is null or { Count: 0 })
        {
            return;
        }

        if (attachmentRepository is null)
        {
            throw new ChatValidationException("Attachments are not available.");
        }

        var maxPer = ParseInt(AttachmentsMaxPerMessageKey, DefaultAttachmentsMaxPerMessage);
        if (attachmentIds.Count > maxPer)
        {
            throw new ChatValidationException($"At most {maxPer} attachments per message.");
        }

        var rawIds = attachmentIds.Distinct(StringComparer.Ordinal).ToList();
        var ids = rawIds.Select(ChatAttachmentId.From).ToList();
        var rows = await attachmentRepository.Query
            .Where(a => a.ConversationId == conversationId && ids.Contains(a.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count != rawIds.Count)
        {
            throw new ChatValidationException("Unknown attachment id in attachmentIds.");
        }

        if (rows.Any(a => a.MessageId is not null))
        {
            throw new ChatConflictException("An attachment is already bound to a message.");
        }

        foreach (var row in rows)
        {
            row.Bind(messageId, UtcNow);
        }

        await attachmentRepository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Descriptor line appended to a message's wire text (RF-002).</summary>
    private static string DescribeAttachments(IReadOnlyList<ChatAttachment> attachments) =>
        string.Concat(attachments.Select(a =>
            $"\n[attachment id={a.Id.Value} name={a.FileName} type={a.ContentType} bytes={a.ByteSize} — read via attach://{a.Id.Value}]"));

    /// <summary>RF-004: image/* attachments become data-URL wire parts.</summary>
    private IReadOnlyList<string>? AttachmentImageDataUrls(IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachmentStore is null)
        {
            return null;
        }

        List<string>? urls = null;
        foreach (var attachment in attachments.Where(
            a => a.ContentType.StartsWith("image/", StringComparison.Ordinal)))
        {
            var bytes = attachmentStore.ReadBytes(attachment.StoragePath);
            if (bytes is { Length: > 0 })
            {
                (urls ??= []).Add($"data:{attachment.ContentType};base64,{Convert.ToBase64String(bytes)}");
            }
        }

        return urls;
    }

    // ---- SPEC-20261005-chat-attachments-feedback: feedback (RNF-003 log-only) ----

    /// <summary>
    /// RF-005: upserts the 👍/👎 + category/note of an assistant message.
    /// <paramref name="expectedVersion"/> is CAS — 0 creates/forces; a stale
    /// value on an existing row → 409.
    /// </summary>
    public async Task<ChatFeedbackDto> PutMessageFeedbackAsync(
        string messageId, string rating, string? category, string? note,
        long expectedVersion, CancellationToken ct = default)
    {
        if (feedbackRepository is null)
        {
            throw new ChatValidationException("Feedback is not available.");
        }

        var message = await messages.GetAsync(ChatMessageId.From(messageId), ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Message '{messageId}' not found.");

        // Open question #3: assistant messages only.
        if (message.Role != ChatMessageRole.Assistant)
        {
            throw new ChatValidationException("Feedback applies to assistant messages only.");
        }

        var row = await feedbackRepository.Query
            .FirstOrDefaultAsync(f => f.MessageId == message.Id, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = ChatMessageFeedback.Create(
                ChatMessageFeedbackId.NewGuid(), message.Id, message.ConversationId,
                rating, category, note, UtcNow);
            await feedbackRepository.AddAsync(row, ct).ConfigureAwait(false);
        }
        else
        {
            if (row.Version != expectedVersion)
            {
                throw new ChatConflictException(
                    $"Feedback for message '{messageId}' changed concurrently (expected version {expectedVersion}, got {row.Version}).");
            }

            row.Rate(rating, category, note, UtcNow);
        }

        await feedbackRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ChatFeedbackDto(row.Rating, row.Category, row.Note, row.Version, row.UpdatedAt);
    }

    /// <summary>RF-005: clears the feedback of a message — idempotent.</summary>
    public async Task DeleteMessageFeedbackAsync(string messageId, CancellationToken ct = default)
    {
        if (feedbackRepository is null)
        {
            return;
        }

        var row = await feedbackRepository.Query
            .FirstOrDefaultAsync(f => f.MessageId == ChatMessageId.From(messageId), ct).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        await feedbackRepository.DeleteAsync(row, ct).ConfigureAwait(false);
        await feedbackRepository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-001/RF-002/RF-003: branches the
    /// conversation at <paramref name="messageId"/> — copies the prefix
    /// (inclusive, order preserved, ids regenerated with
    /// <c>ForkedFromMessageId</c> back-pointers and summary bounds remapped),
    /// records the lineage, copies PlanMode/agent context. Runs, approvals
    /// and notify state do NOT copy — the fork starts idle.
    /// </summary>
    public async Task<ChatConversationDto> ForkConversationAsync(
        string conversationId, string messageId, CancellationToken ct = default)
    {
        var sourceId = ChatConversationId.From(conversationId);
        var source = await conversations.GetAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");

        var rows = await messages.Query
            .Where(m => m.ConversationId == source.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var boundIndex = rows.FindIndex(m => m.Id.Value == messageId);
        if (boundIndex < 0)
        {
            throw new ChatValidationException(
                $"Message '{messageId}' does not belong to conversation '{conversationId}'.");
        }

        var now = UtcNow;
        var fork = ChatConversation.Create(
            ChatConversationId.NewGuid(), source.ProviderId, source.ProviderName,
            source.Model, $"{source.Title} (fork)", now);
        fork.MarkForkedFrom(source.Id.Value, messageId);
        if (source.PlanMode == ChatPlanModes.On)
        {
            fork.SetPlanMode(true, now);
        }

        fork.SetPermissionPreset(source.PermissionPreset, now);
        fork.SetAgentContext(
            source.AgentCli, source.RepositoryFullName, source.WorkspacePath, source.AgentModel);
        await conversations.AddAsync(fork, ct).ConfigureAwait(false);

        // Two passes: mint ids first so summary SupersedesUntilMessageId
        // bounds remap to the copied rows instead of dangling on the source.
        var prefix = rows.Take(boundIndex + 1).ToList();
        var newIds = prefix.Select(_ => ChatMessageId.NewGuid()).ToList();
        var idMap = prefix.Zip(newIds, (m, id) => (m.Id.Value, Id: id.Value))
            .ToDictionary(x => x.Value, x => x.Id, StringComparer.Ordinal);
        foreach (var (message, newId) in prefix.Zip(newIds))
        {
            var remappedBound = message.SupersedesUntilMessageId is not null
                ? idMap.GetValueOrDefault(message.SupersedesUntilMessageId)
                : null;
            var cloned = ChatMessage.CreateForked(newId, fork.Id, message, remappedBound);
            await messages.AddAsync(cloned, ct).ConfigureAwait(false);
            await IndexMessageSafeAsync(cloned, ct).ConfigureAwait(false);
        }

        // SPEC-20261005-chat-attachments-feedback open question #2: attachment
        // rows copy over (new ids, file bytes copied — append-only store keeps
        // attach:// resolvable without a DB lookup).
        if (attachmentRepository is not null && attachmentStore is not null)
        {
            var sourceMessageIds = prefix.Select(m => (ChatMessageId?)m.Id).ToList();
            var sourceAttachments = await attachmentRepository.Query
                .Where(a => a.MessageId != null && sourceMessageIds.Contains(a.MessageId))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var attachment in sourceAttachments)
            {
                var bytes = attachmentStore.ReadBytes(attachment.StoragePath);
                if (bytes is null)
                {
                    continue; // file swept/lost — drop the reference
                }

                var newId = ChatAttachmentId.NewGuid();
                var (storagePath, sha256) = attachmentStore.Save(newId.Value, attachment.ContentType, bytes);
                var copy = ChatAttachment.Create(
                    newId, fork.Id, attachment.FileName, attachment.ContentType,
                    bytes.LongLength, storagePath, sha256, now);
                copy.RebindTo(ChatMessageId.From(idMap[attachment.MessageId!.Value]));
                await attachmentRepository.AddAsync(copy, ct).ConfigureAwait(false);
            }
        }

        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(fork, null);
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-006: claims pending steers (oldest
    /// first), persists each as a <c>Kind=steer</c> user message and appends
    /// to the live wire. Serialized with the run — the drain runs inside the
    /// executor's loop, never concurrently.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> DrainSteersAsync(
        ChatRun run,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pending = await steerRepository.Query
            .Where(s => s.RunId == run.Id && s.ClaimedAt == null)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            yield break;
        }

        var now = UtcNow;
        foreach (var steer in pending)
        {
            // Atomic claim — a cancel that already deleted the row (or raced
            // the claim) affects 0 rows and the steer is skipped.
            var claimed = await steerRepository.Query
                .Where(s => s.Id == steer.Id && s.ClaimedAt == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.ClaimedAt, now), ct)
                .ConfigureAwait(false) > 0;
            if (!claimed)
            {
                continue;
            }

            var message = ChatMessage.CreateSteer(conversation.Id, steer.Content, now);
            await messages.AddAsync(message, ct).ConfigureAwait(false);
            await IndexMessageSafeAsync(message, ct).ConfigureAwait(false);

            // SPEC-20261005-chat-attachments-feedback: steered attachments bind
            // to the steer message and ride the wire as descriptor lines.
            var wireText = steer.Content;
            if (steer.AttachmentIdsJson is not null && attachmentRepository is not null)
            {
                var rawIds = JsonSerializer.Deserialize<List<string>>(steer.AttachmentIdsJson) ?? [];
                var ids = rawIds.Select(ChatAttachmentId.From).ToList();
                var bound = await attachmentRepository.Query
                    .Where(a => a.ConversationId == conversation.Id && a.MessageId == null && ids.Contains(a.Id))
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var attachment in bound)
                {
                    attachment.Bind(message.Id, now);
                }

                if (bound.Count > 0)
                {
                    wireText += DescribeAttachments(bound);
                }
            }

            wire.Add(new OpenAiChatMessage("user", wireText));
            yield return new ChatSteerClaimedEvent(steer.Id.Value, steer.Content);
        }

        conversation.Touch(UtcNow);
        await messages.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var steer in pending)
        {
            await NotifySteerResolvedAsync(conversation.Id.Value, steer.Id.Value, "claimed", ct)
                .ConfigureAwait(false);
        }
    }

    private async Task NotifySteerQueuedAsync(
        string conversationId, string steerId, string content, CancellationToken ct)
    {
        foreach (var notifier in notifiers)
        {
            try
            {
                await notifier.SteerQueuedAsync(conversationId, steerId, content, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "chat steer {SteerId} queued notify failed", steerId);
            }
        }
    }

    private async Task NotifySteerResolvedAsync(
        string conversationId, string steerId, string outcome, CancellationToken ct)
    {
        foreach (var notifier in notifiers)
        {
            try
            {
                await notifier.SteerResolvedAsync(conversationId, steerId, outcome, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "chat steer {SteerId} {Outcome} notify failed", steerId, outcome);
            }
        }
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls RF-002: cooperative pause — parks the
    /// run at the current boundary when the endpoint flagged it, emits
    /// <c>chat.paused</c>, blocks until resume/stop, then flips the row back
    /// to running and emits <c>chat.resumed</c>. Never interrupts in-flight
    /// work. A user stop while parked is swallowed here — the caller's
    /// run-token check turns it into the normal stopped-by-user flow; a host
    /// shutdown rethrows so the dispatcher lands the row as interrupted.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> ParkOnPauseBoundaryAsync(
        ChatRun run, [EnumeratorCancellation] CancellationToken ct,
        CancellationToken stoppingToken)
    {
        if (!runs.ConsumePauseRequest(run.Id.Value))
        {
            yield break;
        }

        run.Pause(UtcNow);
        await runRepository.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
        // Register BEFORE emitting — a resume hitting between chat.paused and
        // the wait below still finds IsParked and releases this executor
        // instead of requeueing the run it is about to keep driving.
        var waiter = runs.RegisterResumeWaiter(run.Id.Value);
        yield return new ChatPausedEvent();

        try
        {
            await waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                throw;
            }

            yield break;
        }
        finally
        {
            // Un-signaled exits (stop/shutdown) drop the stale waiter; a
            // completed resume already removed it — TryRemove is a no-op then.
            runs.AbandonResumeWaiter(run.Id.Value, waiter);
        }

        run.Resume(UtcNow);
        await runRepository.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
        yield return new ChatResumedEvent();
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls RF-002/RF-008: pause flags the live
    /// executor to park at its next boundary; a queued row — or a stale
    /// running row whose executor is gone — flips to paused immediately.
    /// Idempotent: pausing a parked run returns it unchanged.
    /// </summary>
    public async Task<ChatRunDto> PauseRunAsync(
        string conversationId, string runId, CancellationToken ct = default)
    {
        var conversation = ChatConversationId.From(conversationId);
        var run = await runRepository.Query
            .Where(r => r.ConversationId == conversation && r.Id == ChatRunId.From(runId))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Run '{runId}' not found.");

        if (run.Status == ChatRunStatus.Paused)
        {
            return ToDto(run);
        }

        if (run.Status.IsTerminal)
        {
            throw new ChatConflictException($"Run '{runId}' already finished ({run.Status}).");
        }

        if (run.Status == ChatRunStatus.Queued || !runs.IsRunning(conversationId))
        {
            run.Pause(UtcNow);
            await runRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            runs.RequestPause(runId);
        }

        return ToDto(run);
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls RF-003/RF-008: wakes the parked
    /// executor when one is live — it persists the resume and emits
    /// <c>chat.resumed</c>; a row parked without an executor (paused while
    /// queued, or orphaned by a restart) re-enters the dispatcher queue and
    /// the turn is re-driven from the transcript. Idempotent on
    /// running/queued.
    /// </summary>
    public async Task<ChatRunDto> ResumeRunAsync(
        string conversationId, string runId, CancellationToken ct = default)
    {
        var conversation = ChatConversationId.From(conversationId);
        var run = await runRepository.Query
            .Where(r => r.ConversationId == conversation && r.Id == ChatRunId.From(runId))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Run '{runId}' not found.");

        if (run.Status == ChatRunStatus.Running || run.Status == ChatRunStatus.Queued)
        {
            // Idempotent — also kills a pause flag that never reached a
            // boundary (pause→resume inside one step would park forever).
            runs.SignalResume(runId);
            return ToDto(run);
        }

        if (run.Status != ChatRunStatus.Paused)
        {
            throw new ChatConflictException($"Run '{runId}' is not paused ({run.Status}).");
        }

        if (runs.IsParked(runId))
        {
            // Live parked executor persists Resume + chat.resumed itself.
            runs.SignalResume(runId);
            return ToDto(run);
        }

        run.Requeue(UtcNow);
        await runRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        runQueue.Enqueue(new ChatRunWorkItem(run.Id.Value, conversation.Value));
        return ToDto(run);
    }

    /// <summary>
    /// Stops everything pending on the conversation: the live run through the
    /// singleton coordinator (reaches the executor's detached token) plus any
    /// queued runs — the dispatcher never picks them up.
    /// </summary>
    public async Task<bool> StopAsync(string conversationId, CancellationToken ct = default)
    {
        var stopped = runs.Stop(conversationId);
        var conversation = ChatConversationId.From(conversationId);
        var queued = await runRepository.Query
            .Where(r => r.ConversationId == conversation
                && (r.Status == ChatRunStatus.Queued || r.Status == ChatRunStatus.Paused))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var run in queued)
        {
            // SPEC-20261012: a paused row with a live parked executor flips
            // through the normal stop flow — its wait is cancelled, the
            // executor unwinds and the dispatcher marks it stopped. Only
            // executor-less rows are stopped here.
            if (run.Status == ChatRunStatus.Paused && runs.IsParked(run.Id.Value))
            {
                continue;
            }

            run.Stop(UtcNow);
        }

        if (queued.Count > 0)
        {
            await runRepository.SaveChangesAsync(ct).ConfigureAwait(false);
            stopped = true;
        }

        // SPEC-20261005-chat-tool-approval RF-005 auto-cancel: pending
        // approvals of this conversation resolve cancelled so a suspended
        // call unwinds instead of waiting out the timeout.
        var pendingApprovals = await approvalRepository.Query
            .Where(a => a.ConversationId == conversation && a.Status == ChatApprovalStatus.Pending)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var approval in pendingApprovals)
        {
            await TransitionApprovalAsync(
                approval, a => a.Cancel(UtcNow), ChatApprovalVerdict.Denied).ConfigureAwait(false);
        }

        return stopped;
    }

    /// <summary>
    /// The <c>chat.sync</c> payload for an attach (RF-003): durable messages +
    /// the live checkpoint (broadcaster first, DB checkpoint as fallback for a
    /// broadcast that lost its state) + the sequence high-water mark.
    /// </summary>
    public async Task<ChatRunSyncDto?> GetRunSnapshotAsync(
        string conversationId, string runId, CancellationToken ct = default)
    {
        var run = await runRepository.GetAsync(ChatRunId.From(runId), ct).ConfigureAwait(false);
        if (run is null || run.ConversationId.Value != conversationId)
        {
            return null;
        }

        var rows = await messages.Query
            .Where(m => m.ConversationId == run.ConversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var live = broadcaster.GetLive(runId);
        var active = run.Status.IsActive;
        return new ChatRunSyncDto(
            await EnrichMessagesAsync(rows, ct).ConfigureAwait(false),
            ToDto(run),
            live?.Partial ?? (active ? run.PartialContent : null),
            live?.PartialReasoning ?? (active ? run.PartialReasoning : null),
            live?.LastSeq ?? 0,
            // SPEC-20261012 RF-007: fresh attach derives `stalled` without
            // waiting a full threshold — falls back to StartedAt/CreatedAt
            // so a run that never emitted still has a baseline.
            runs.GetLastActivity(runId) ?? run.StartedAt ?? run.CreatedAt,
            ParseInt("Taskboard:Chat:StallThresholdSeconds", 120));
    }

    /// <summary>
    /// <see cref="IChatRunExecutor"/>: the detached turn — same tool loop the
    /// request-bound stream had, minus <c>requestAborted</c>: the run token is
    /// cancelled only by a user stop; the host-lifetime token carries
    /// post-stop persistence and propagates shutdown as an interrupt.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> ExecuteAsync(
        ChatRun run, CancellationTokenSource runCts,
        [EnumeratorCancellation] CancellationToken stoppingToken)
    {
        var conversation = await conversations.GetAsync(run.ConversationId, stoppingToken).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{run.ConversationId.Value}' not found.");
        var provider = await GetProviderSnapshotAsync(conversation.ProviderId, stoppingToken).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Provider '{conversation.ProviderName}' no longer exists.");

        // Shutdown also cancels the stream — TryMoveNextAsync tells a host
        // interrupt (rethrown) from a user stop (StoppedByUser).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token, stoppingToken);
        var ct = linked.Token;

        // SPEC-20261001-chat-capability-registry FR-003: effective tool set —
        // disabled capabilities never reach the provider payload.
        var toolSet = await capabilities.ResolveToolSetAsync(ct).ConfigureAwait(false);
        var wire = await BuildTranscriptAsync(conversation, toolSet, run.TriggerMessageId, ct).ConfigureAwait(false);
        var toolDefs = BuildToolDefinitions(toolSet);
        var state = new TurnState();

        // SPEC-20261005-chat-attachments-feedback RF-007: run-start workspace
        // snapshot — git-aware when inside a worktree; the run-end diff plus
        // the file-edit tracker become the deliverables card.
        var workspacePath = !string.IsNullOrWhiteSpace(conversation.WorkspacePath)
            ? conversation.WorkspacePath
            : workspace.ResolveCardWorkdir(conversation.RepositoryFullName, out _);
        var wsSnapshot = workspaceDiff is null || workspacePath is null
            ? null
            : await workspaceDiff.SnapshotAsync(workspacePath, ct).ConfigureAwait(false);

        var maxIterations = ParseInt("Taskboard:Chat:MaxToolIterations", 8);
        // Reasoning models burn output tokens on reasoning_content before the
        // answer — without an explicit budget gateways cap too low and the
        // turn ends with empty content.
        var maxTokens = ParseInt("Taskboard:Chat:MaxTokens", 4096);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // SPEC-20261012-chat-run-controls RF-002: cooperative park at a
            // turn boundary — before compaction and the provider call.
            await foreach (var ev in ParkOnPauseBoundaryAsync(run, ct, stoppingToken).ConfigureAwait(false))
            {
                yield return ev;
            }

            if (runCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                state.Error = "stopped by user";
                break;
            }

            // SPEC-20261005-chat-context-management RF-002: pressure check
            // before EACH provider call — prune (+summarize) the wire when it
            // crosses the effective budget; wire-only, history never mutates.
            await foreach (var ev in MaybeCompactWireAsync(run, provider, conversation, wire, toolSet, state, stoppingToken, ct)
                .ConfigureAwait(false))
            {
                yield return ev;
            }

            // B-02: deltas are yielded as they arrive — subscribers see live
            // progress instead of a burst after the provider finishes.
            var stream = new StreamOutcome();
            var consume = new ConsumeContext(
                provider, conversation, wire, toolDefs, maxTokens, stream, runCts, stoppingToken);
            await foreach (var ev in ConsumeStreamAsync(consume, ct).ConfigureAwait(false))
            {
                yield return ev;
            }

            UpdateState(stream, state);
            // RF-001 anchor: the provider's own prompt_tokens replaces the
            // heuristic baseline; later estimates = anchor + delta growth.
            if (stream.Usage?.PromptTokens is > 0)
            {
                state.AnchorTokens = stream.Usage.PromptTokens.Value;
                state.AnchorCount = wire.Count;
            }

            if (stream.ProviderError is not null)
            {
                // RF-005: context_length_exceeded mid-run → force compaction
                // and retry the same step once; a second failure surfaces.
                if (!state.OverflowRetried && IsContextLengthError(stream.ProviderError))
                {
                    state.OverflowRetried = true;
                    state.Error = null;
                    await foreach (var ev in ForceCompactWireAsync(run, provider, conversation, wire, toolSet, state, stoppingToken, ct)
                        .ConfigureAwait(false))
                    {
                        yield return ev;
                    }

                    continue;
                }

                break;
            }

            // Flush the inline-markup filter tail — held-back marker prefixes
            // resolve here (visible) or are dropped (partial markup).
            if (stream.Complete() is { Length: > 0 } tail)
            {
                yield return new ChatDeltaEvent(tail);
            }

            // After a user stop the run token is cancelled — the partial reply
            // is still persisted using the host token, which stays alive.
            var persistCt = stream.StoppedByUser ? stoppingToken : ct;
            var toolCalls = await PersistAssistantTurnAsync(
                    conversation, wire, stream, state.TokensIn, state.TokensOut, persistCt)
                .ConfigureAwait(false);
            yield return new ChatPersistedEvent();

            if (toolCalls.Count == 0)
            {
                break;
            }

            await foreach (var ev in RunToolCallsAsync(run, toolCalls, provider, toolSet, conversation, wire, state, stoppingToken, ct)
                .ConfigureAwait(false))
            {
                yield return ev;
            }

            if (runCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                // User stop while a tool call was executing or suspended on an
                // approval — the tool loop broke out early (the gate already
                // auto-cancelled the pending row); end the turn as stopped.
                state.Error = "stopped by user";
                break;
            }

            // SPEC-20261005-chat-fork-steering RF-006: drain the steer inbox
            // at the tool-result boundary — each claimed steer persists as a
            // user message and lands on the wire before the next model call.
            await foreach (var ev in DrainSteersAsync(run, conversation, wire, ct)
                .ConfigureAwait(false))
            {
                yield return ev;
            }

            conversation.Touch(UtcNow);
            await conversations.SaveChangesAsync(persistCt).ConfigureAwait(false);
        }

        // RF-007: deliverables — tracked file-tool edits + present
        // declarations + the git diff over the start snapshot. Persists rows
        // and streams chat.deliverables so the card renders live AND after
        // reload.
        var deliverables = await CollectDeliverablesAsync(
            run, conversation, workspacePath, wsSnapshot,
            stoppingToken.IsCancellationRequested ? CancellationToken.None : stoppingToken)
            .ConfigureAwait(false);
        if (deliverables.Count > 0)
        {
            yield return new ChatDeliverablesEvent(deliverables);
        }

        await conversations.SaveChangesAsync(stoppingToken.IsCancellationRequested ? CancellationToken.None : stoppingToken)
            .ConfigureAwait(false);
        yield return new ChatDoneEvent(state.TokensIn, state.TokensOut, state.Error is null ? "stop" : "error", state.Error);
    }

    /// <summary>
    /// RF-007: merges the file-edit tracker drain with the workspace git diff
    /// (dedupe by path — a tracked tool-edit row wins over the coarser git
    /// line counts) and persists <c>ChatRunDeliverable</c> rows.
    /// </summary>
    private async Task<IReadOnlyList<ChatDeliverableDto>> CollectDeliverablesAsync(
        ChatRun run, ChatConversation conversation, string? workspacePath,
        ChatWorkspaceSnapshot? snapshot, CancellationToken ct)
    {
        var merged = new List<ChatDeliverableDto>();
        var tracked = editTracker?.Drain(run.Id.Value) ?? [];
        foreach (var edit in tracked)
        {
            var (added, removed) = CountDiff(edit.BeforeContent, edit.AfterContent);
            merged.Add(new ChatDeliverableDto(edit.Path, added, removed, edit.Source, edit.Summary));
        }

        if (snapshot is not null && workspaceDiff is not null && workspacePath is not null)
        {
            var trackedPaths = tracked.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var diff = await workspaceDiff.DiffAsync(workspacePath, snapshot, ct).ConfigureAwait(false);
            merged.AddRange(diff.Where(d => !trackedPaths.Contains(d.Path)));
        }

        if (merged.Count > 0 && deliverableRepository is not null)
        {
            foreach (var dto in merged)
            {
                await deliverableRepository.AddAsync(
                    ChatRunDeliverable.Create(
                        ChatRunDeliverableId.NewGuid(), run.Id, conversation.Id,
                        dto.Path, dto.AddedLines, dto.RemovedLines, dto.Source, dto.Summary, UtcNow),
                    ct).ConfigureAwait(false);
            }

            await deliverableRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return merged;
    }

    /// <summary>
    /// Whole-file snapshot accounting — without a git diff we can only claim
    /// line counts for created/deleted files; in-place edits show no counts.
    /// </summary>
    private static (int? Added, int? Removed) CountDiff(string? before, string? after) => (before, after) switch
    {
        (null, { } a) => (a.Split('\n').Length, null),
        ({ } b, null) => (null, b.Split('\n').Length),
        _ => (null, null),
    };

    /// <summary>Mutable per-turn accumulators carried across tool-call iterations.</summary>
    private sealed class TurnState
    {
        public int? TokensIn { get; set; }

        public int? TokensOut { get; set; }

        public string? Error { get; set; }

        /// <summary>RF-007: compaction passes performed in this run.</summary>
        public int Compactions { get; set; }

        /// <summary>RF-001: provider-reported prompt_tokens at <see cref="AnchorCount"/> wire entries.</summary>
        public int? AnchorTokens { get; set; }

        /// <summary>RF-001: wire length the anchor covered.</summary>
        public int AnchorCount { get; set; }

        /// <summary>RF-005: overflow retry is a single shot.</summary>
        public bool OverflowRetried { get; set; }

        /// <summary>RF-006: deterministic spill id sequence per run.</summary>
        public int SpillSeq { get; set; }
    }

    private static void UpdateState(StreamOutcome stream, TurnState state)
    {
        if (stream.Usage is { } usage)
        {
            state.TokensIn = usage.PromptTokens ?? state.TokensIn;
            state.TokensOut = usage.CompletionTokens ?? state.TokensOut;
        }

        if (stream.StoppedByUser)
        {
            state.Error = "stopped by user";
        }

        if (stream.ProviderError is not null)
        {
            state.Error = stream.ProviderError.Message;
        }
    }

    /// <summary>Persists the assistant turn and appends it to the provider wire transcript.</summary>
    private async Task<List<OpenAiToolCall>> PersistAssistantTurnAsync(
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        StreamOutcome stream,
        int? tokensIn,
        int? tokensOut,
        CancellationToken persistCt)
    {
        var toolCalls = stream.MaterializeToolCalls();
        var content = stream.AssistantContent.ToString();
        var assistantMessage = ChatMessage.CreateAssistant(
            conversation.Id, content,
            toolCalls.Count > 0
                ? JsonSerializer.Serialize(toolCalls.Select(tc => new { id = tc.Id, name = tc.Name, arguments = tc.ArgumentsJson }).ToList())
                : null,
            tokensIn, tokensOut, conversation.Model, UtcNow);
        await messages.AddAsync(assistantMessage, persistCt).ConfigureAwait(false);
        await IndexMessageSafeAsync(assistantMessage, persistCt).ConfigureAwait(false);
        await messages.SaveChangesAsync(persistCt).ConfigureAwait(false);

        wire.Add(new OpenAiChatMessage(ChatMessageRole.Assistant.Value, content, toolCalls));
        return toolCalls;
    }

    // Accumulated output of one provider stream pass: assistant text,
    // tool-call fragments, usage, and the terminal failure. Deltas are
    // streamed live by ConsumeStreamAsync (B-02), not replayed from here.
    private sealed class StreamOutcome
    {
        // SPEC-20261001-ai-chat-openwebui: DSML/<tool_call> markup emitted
        // inline by some models is filtered out of the visible stream and
        // later materialized into real tool calls.
        private readonly InlineToolCallMarkup _inlineMarkup = new();

        public System.Text.StringBuilder AssistantContent { get; } = new();

        public SortedDictionary<int, (string? Id, string? Name, System.Text.StringBuilder Args)> ToolAccumulator { get; } = new();

        public OpenAiUsage? Usage { get; private set; }

        public ChatProviderException? ProviderError { get; set; }

        public bool StoppedByUser { get; set; }

        /// <summary>Returns the visible content delta when the chunk carried one.</summary>
        public string? AccumulateChunk(OpenAiStreamEvent chunk)
        {
            var delta = chunk.ContentDelta is { Length: > 0 } content ? content : null;
            string? visible = null;
            if (delta is not null)
            {
                visible = _inlineMarkup.Feed(delta);
                if (visible.Length == 0)
                {
                    visible = null;
                }
                else
                {
                    AssistantContent.Append(visible);
                }
            }

            if (chunk.ToolCallDeltas is { Count: > 0 } deltas)
            {
                foreach (var tc in deltas)
                {
                    var current = ToolAccumulator.TryGetValue(tc.Index, out var value)
                        ? value
                        : (null, null, new System.Text.StringBuilder());
                    ToolAccumulator[tc.Index] = (
                        tc.Id ?? current.Item1,
                        tc.Name ?? current.Item2,
                        Append(current.Item3, tc.ArgumentsDelta));
                }
            }

            if (chunk.Usage is { } usage)
            {
                Usage = usage;
            }

            return visible;
        }

        /// <summary>Flushes the markup filter tail into <see cref="AssistantContent"/>; returns it for a final delta event.</summary>
        public string Complete()
        {
            var tail = _inlineMarkup.Flush();
            AssistantContent.Append(tail);
            return tail;
        }

        private static System.Text.StringBuilder Append(System.Text.StringBuilder builder, string? value)
        {
            builder.Append(value ?? string.Empty);
            return builder;
        }

        public List<OpenAiToolCall> MaterializeToolCalls()
        {
            var structured = ToolAccumulator
                .Select(kv => new OpenAiToolCall(
                    Id: kv.Value.Id ?? $"call_{kv.Key}",
                    Name: kv.Value.Name ?? "unknown",
                    ArgumentsJson: string.IsNullOrWhiteSpace(kv.Value.Args.ToString()) ? "{}" : kv.Value.Args.ToString()))
                .ToList();
            return structured.Count > 0 ? structured : _inlineMarkup.MaterializeCalls().ToList();
        }
    }

    // B-02: streams provider chunks as they arrive — each content delta is
    // yielded immediately and a keepalive ChatStatusEvent("streaming") is
    // emitted when the provider goes quiet, so the SSE connection never idles.
    private sealed record ConsumeContext(
        ChatProviderSnapshot Provider,
        ChatConversation Conversation,
        List<OpenAiChatMessage> Wire,
        List<OpenAiToolDefinition> ToolDefs,
        int MaxTokens,
        StreamOutcome Outcome,
        CancellationTokenSource Stop,
        CancellationToken HostStopping);

    private async IAsyncEnumerable<ChatStreamEvent> ConsumeStreamAsync(
        ConsumeContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var heartbeat = TimeSpan.FromSeconds(
            ParseInt("Taskboard:Chat:SseHeartbeatSeconds", 15));
        await using var enumerator = client.StreamChatAsync(
            ctx.Provider.BaseUrl, ctx.Provider.ApiKey, ctx.Conversation.Model, ctx.Wire,
            ctx.ToolDefs.Count > 0 ? ctx.ToolDefs : null, ctx.MaxTokens, ct).GetAsyncEnumerator(ct);

        while (true)
        {
            var next = enumerator.MoveNextAsync().AsTask();
            await foreach (var ev in WaitHeartbeatAsync(next, heartbeat, ct).ConfigureAwait(false))
            {
                yield return ev;
            }

            if (!await TryMoveNextAsync(next, ctx.Outcome, ctx.Stop, ctx.HostStopping).ConfigureAwait(false))
            {
                yield break;
            }

            if (ctx.Outcome.AccumulateChunk(enumerator.Current) is { } delta)
            {
                yield return new ChatDeltaEvent(delta);
            }

            if (enumerator.Current.ReasoningDelta is { Length: > 0 } reasoning)
            {
                yield return new ChatReasoningEvent(reasoning);
            }
        }
    }

    // Heartbeat loop runs without a catch around the yield — a cancelled
    // delay breaks out and the real exception surfaces on `await next`.
    private static async IAsyncEnumerable<ChatStreamEvent> WaitHeartbeatAsync(
        Task<bool> next,
        TimeSpan heartbeat,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!next.IsCompleted)
        {
            var completed = await Task.WhenAny(next, Task.Delay(heartbeat, ct)).ConfigureAwait(false);
            if (completed == next || ct.IsCancellationRequested)
            {
                yield break;
            }

            yield return new ChatStatusEvent("streaming", null);
        }
    }

    private static async Task<bool> TryMoveNextAsync(
        Task<bool> next,
        StreamOutcome outcome,
        CancellationTokenSource cts,
        CancellationToken hostStopping)
    {
        try
        {
            return await next.ConfigureAwait(false);
        }
        catch (ChatProviderException ex)
        {
            outcome.ProviderError = ex;
        }
        catch (HttpRequestException ex)
        {
            outcome.ProviderError = new ChatProviderException($"Provider unreachable: {ex.Message}", 502);
        }
        catch (OperationCanceledException) when (hostStopping.IsCancellationRequested)
        {
            // SPEC-20261005: host shutdown is not a user stop — propagate so
            // the dispatcher marks the run Interrupted instead of Completed.
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            outcome.StoppedByUser = true;
        }

        return false;
    }

    private async IAsyncEnumerable<ChatStreamEvent> RunToolCallsAsync(
        ChatRun run,
        List<OpenAiToolCall> toolCalls,
        ChatProviderSnapshot provider,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        TurnState state,
        CancellationToken stoppingToken,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var toolCall in toolCalls)
        {
            // SPEC-20261012-chat-run-controls RF-002: park between tool
            // results — no tool executes while the run is paused.
            await foreach (var ev in ParkOnPauseBoundaryAsync(run, ct, stoppingToken).ConfigureAwait(false))
            {
                yield return ev;
            }

            if (ct.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                yield break;
            }

            // SPEC-20261005-chat-tool-approval RF-002: the gate resolves the
            // per-call policy BEFORE execution — ask parks on a persisted
            // ChatApproval, deny becomes a synthetic refusal, allow falls
            // through to the normal path.
            var gateJson = await ApplyApprovalGateAsync(run, conversation, provider, toolSet, toolCall, ct, stoppingToken)
                .ConfigureAwait(false);
            if (gateJson is { Abort: true })
            {
                yield break;
            }

            // SPEC-20261013 RF-004: medium auto-elevations — the call runs,
            // but the tool card gets the risk badge.
            if (gateJson is { Notice: { } notice })
            {
                yield return new ChatRiskNoticeEvent(
                    toolCall.Id, toolCall.Name, notice.RiskValue, notice.Reason);
            }

            if (gateJson?.ResultJson is not null)
            {
                var deniedJson = gateJson.ResultJson;
                var deniedReason = gateJson.RefusalReason;
                var deniedRefused = gateJson.Refused;
                yield return new ChatToolCallEvent(toolCall.Name, toolCall.ArgumentsJson);
                yield return new ChatToolResultEvent(toolCall.Name, deniedJson, deniedRefused, deniedReason);
                var deniedMessage = ChatMessage.CreateTool(
                    conversation.Id, toolCall.Id, toolCall.Name, deniedJson, refused: deniedRefused, UtcNow);
                await messages.AddAsync(deniedMessage, ct).ConfigureAwait(false);
                await IndexMessageSafeAsync(deniedMessage, ct).ConfigureAwait(false);
                await messages.SaveChangesAsync(ct).ConfigureAwait(false);
                wire.Add(new OpenAiChatMessage("tool", deniedJson, ToolCallId: toolCall.Id, Name: toolCall.Name));
                continue;
            }

            // FR-003: live status — the "running" event reaches the client
            // before the (possibly long) tool call completes; tool-reported
            // activity is drained from a channel while it executes.
            var activity = Channel.CreateUnbounded<ChatStreamEvent>();
            yield return new ChatStatusEvent("running_tool", toolCall.Name);
            var toolTask = ExecuteToolAsync(toolCall, provider, toolSet, conversation, run, activity.Writer, ct);
            while (!toolTask.IsCompleted)
            {
                while (activity.Reader.TryRead(out var progress))
                {
                    yield return progress;
                }

                await Task.Delay(150, ct).ConfigureAwait(false);
            }

            while (activity.Reader.TryRead(out var progress))
            {
                yield return progress;
            }

            var (resultJson, refused, refusalReason, imageUrls) = await toolTask.ConfigureAwait(false);
            yield return new ChatStatusEvent("idle", null);
            yield return new ChatToolCallEvent(toolCall.Name, toolCall.ArgumentsJson);
            yield return new ChatToolResultEvent(toolCall.Name, resultJson, refused, refusalReason);

            var toolMessage = ChatMessage.CreateTool(conversation.Id, toolCall.Id, toolCall.Name, resultJson, refused, UtcNow);
            // B-17: tools that persist files (generate_image) return the path in
            // the result payload — attach it so the transcript renders the image.
            if (TryReadImagePath(resultJson) is { } imagePath)
            {
                toolMessage.AttachImage(imagePath, UtcNow);
            }

            await messages.AddAsync(toolMessage, ct).ConfigureAwait(false);
            await IndexMessageSafeAsync(toolMessage, ct).ConfigureAwait(false);
            await messages.SaveChangesAsync(ct).ConfigureAwait(false);
            // RF-006: oversized results spill to disk — the wire keeps a capped
            // head + spill:// pointer (persisted history stays whole, RNF-001).
            var wireResult = SpillToolResult(run, resultJson, state);
            // SPEC-20261005-chat-attachments-feedback RF-004: read_image /
            // attach:// results land as image_url parts on the wire tool
            // message; the persisted row keeps JSON only.
            wire.Add(new OpenAiChatMessage(
                "tool", wireResult, ToolCallId: toolCall.Id, Name: toolCall.Name,
                ImageUrls: imageUrls));
        }
    }

    private static string? TryReadImagePath(string resultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            return doc.RootElement.TryGetProperty("imagePath", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- SPEC-20261005-chat-context-management: pressure, prune, spill, summarize ----

    /// <summary>
    /// RF-002/RF-007: estimate wire pressure, run the compaction pipeline when
    /// it crosses the effective budget, then emit <c>chat.pressure</c> (SSE +
    /// <c>run.pressure</c> hub fan-out for the sidebar badge).
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> MaybeCompactWireAsync(
        ChatRun run,
        ChatProviderSnapshot provider,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        TurnState state,
        CancellationToken stoppingToken,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!ChatContextBudget.Enabled(configuration))
        {
            yield break;
        }

        var limit = ChatContextBudget.CompactLimit(configuration);
        var compacted = false;
        if (EstimatePressure(wire, state) >= limit)
        {
            compacted = await CompactWireAsync(run, provider, conversation, wire, toolSet, state, stoppingToken, ct)
                .ConfigureAwait(false);
            if (compacted)
            {
                state.Compactions++;
                run.RecordContextStats(limit, state.Compactions);
            }
        }

        var estimate = EstimatePressure(wire, state);
        foreach (var notifier in notifiers)
        {
            // run.pressure hub fan-out (sidebar badge) — notifiers tolerate no-op.
            await notifier.RunPressureAsync(
                run.Id.Value, conversation.Id.Value, estimate, limit, compacted, ct).ConfigureAwait(false);
        }

        yield return new ChatPressureEvent(estimate, limit, compacted);
    }

    /// <summary>RF-005: compaction forced by context_length_exceeded.</summary>
    private async IAsyncEnumerable<ChatStreamEvent> ForceCompactWireAsync(
        ChatRun run,
        ChatProviderSnapshot provider,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        TurnState state,
        CancellationToken stoppingToken,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!ChatContextBudget.Enabled(configuration))
        {
            yield break;
        }

        if (await CompactWireAsync(run, provider, conversation, wire, toolSet, state, stoppingToken, ct)
            .ConfigureAwait(false))
        {
            var limit = ChatContextBudget.CompactLimit(configuration);
            state.Compactions++;
            run.RecordContextStats(limit, state.Compactions);
            yield return new ChatPressureEvent(EstimatePressure(wire, state), limit, Compacted: true);
        }
    }

    private int EstimatePressure(List<OpenAiChatMessage> wire, TurnState state) =>
        state.AnchorTokens is { } anchor
            ? TokenPressureEstimator.AnchoredEstimate(wire, anchor, state.AnchorCount)
            : TokenPressureEstimator.EstimateTokens(wire);

    /// <summary>RF-003 prune → RF-004 summarize when still over (RNF-002 safe).</summary>
    private async Task<bool> CompactWireAsync(
        ChatRun run,
        ChatProviderSnapshot provider,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        TurnState state,
        CancellationToken stoppingToken,
        CancellationToken ct)
    {
        var pruned = PruneWire(run, wire, state);
        var summarized = false;
        if (EstimatePressure(wire, state) >= ChatContextBudget.CompactLimit(configuration))
        {
            summarized = await SummarizePrefixAsync(
                provider, conversation, wire, toolSet, run.TriggerMessageId, stoppingToken, ct)
                .ConfigureAwait(false);
        }

        return pruned > 0 || summarized;
    }

    /// <summary>
    /// RF-003: tool-result wire entries older than the last KeepRecentTurns
    /// user turns spill (when large) or collapse to a tombstone — wire-only
    /// (RNF-001); wire[0] (system prompt) is never touched.
    /// </summary>
    private int PruneWire(ChatRun run, List<OpenAiChatMessage> wire, TurnState state)
    {
        var keepRecent = ChatContextBudget.KeepRecentTurns(configuration);
        var cutoff = 0;
        var users = 0;
        for (var i = wire.Count - 1; i >= 0; i--)
        {
            if (wire[i].Role == "user" && ++users == keepRecent)
            {
                cutoff = i;
                break;
            }
        }

        var pruned = 0;
        for (var i = 1; i < cutoff; i++)
        {
            if (wire[i].Role != "tool")
            {
                continue;
            }

            var content = wire[i].Content ?? string.Empty;
            wire[i] = wire[i] with
            {
                Content = Encoding.UTF8.GetByteCount(content) > ChatContextBudget.SpillBytes(configuration)
                    && spillStore is not null
                        ? SpillToolResult(run, content, state)
                        : ChatContextBudget.PrunedTombstone,
            };
            pruned++;
        }

        return pruned;
    }

    /// <summary>RF-006: spill an oversized result to disk → head + pointer.</summary>
    private string SpillToolResult(ChatRun run, string content, TurnState state)
    {
        if (spillStore is null
            || Encoding.UTF8.GetByteCount(content) <= ChatContextBudget.SpillBytes(configuration))
        {
            return content;
        }

        var spillId = spillStore.Save(run.Id.Value, ++state.SpillSeq, content);
        var headChars = Math.Min(ChatContextBudget.SpillHeadBytes(configuration), content.Length);
        return $"{content[..headChars]}\n[… {content.Length} chars truncated — full output saved at spill://{spillId}; page it back with read_file path=\"spill://{spillId}\" + offset/limit]";
    }

    /// <summary>
    /// RF-004: condense stored history up to the (KeepRecentTurns)-th latest
    /// user turn into a persisted summary row, then rebuild the wire under the
    /// new bound. RNF-002: provider failures fall back to prune-only.
    /// </summary>
    private async Task<bool> SummarizePrefixAsync(
        ChatProviderSnapshot provider,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatMessageId? triggerMessageId,
        CancellationToken stoppingToken,
        CancellationToken ct)
    {
        var keepRecent = ChatContextBudget.KeepRecentTurns(configuration);
        var rows = await messages.Query
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var users = 0;
        var boundIndex = -1;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].Role == ChatMessageRole.User && ++users >= keepRecent)
            {
                boundIndex = i - 1;
                break;
            }
        }

        if (boundIndex < 0)
        {
            return false;
        }

        // Summaries chain: the prefix starts right after the latest summary's
        // bound so later compactions only cover the turns added since.
        var latestSummary = rows.LastOrDefault(m => m.Kind == ChatMessageKinds.Summary);
        var prefixStart = 0;
        if (latestSummary?.SupersedesUntilMessageId is { } prevBound)
        {
            var prevIndex = rows.FindIndex(m => m.Id.Value == prevBound);
            if (prevIndex >= 0)
            {
                prefixStart = prevIndex + 1;
            }
        }

        if (prefixStart > boundIndex)
        {
            return false; // nothing new to condense
        }

        var prefix = rows.GetRange(prefixStart, boundIndex - prefixStart + 1)
            .Where(m => m.Role != ChatMessageRole.System)
            .ToList();
        if (prefix.Count == 0)
        {
            return false;
        }

        var prefixText = new StringBuilder();
        foreach (var m in prefix)
        {
            prefixText.Append('[').Append(m.Role.Value).Append("] ")
                .AppendLine(InlineToolCallMarkup.StripBlocks(m.Content));
        }

        string? summaryText;
        try
        {
            summaryText = await SummarizeAsync(provider, conversation, prefixText.ToString(), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ChatProviderException ex)
        {
            // RNF-002: a summarization failure never kills the run.
            logger.LogWarning(ex, "Chat context summarization failed — prune-only fallback.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            return false;
        }

        var summary = ChatMessage.CreateSummary(
            conversation.Id,
            $"{ChatContextBudget.CompactedNote}\n\n{summaryText.Trim()}",
            rows[boundIndex].Id.Value, UtcNow);
        await messages.AddAsync(summary, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(summary, ct).ConfigureAwait(false);
        await messages.SaveChangesAsync(ct).ConfigureAwait(false);

        // RNF-001: rebuild the wire under the new bound — history stays whole.
        var rebuilt = await BuildTranscriptAsync(conversation, toolSet, triggerMessageId, ct)
            .ConfigureAwait(false);
        wire.Clear();
        wire.AddRange(rebuilt);
        return true;
    }

    /// <summary>RF-004: one non-tool provider call producing the summary text.</summary>
    private async Task<string?> SummarizeAsync(
        ChatProviderSnapshot provider, ChatConversation conversation, string prefixText, CancellationToken ct)
    {
        var requestMessages = new List<OpenAiChatMessage>
        {
            new("system", ChatContextBudget.SummaryPrompt(configuration)),
            new("user", prefixText),
        };
        var sb = new StringBuilder();
        await foreach (var ev in client.StreamChatAsync(
            provider.BaseUrl, provider.ApiKey, conversation.Model,
            requestMessages, tools: null,
            maxTokens: ChatContextBudget.SummaryMaxTokens(configuration), ct)
            .ConfigureAwait(false))
        {
            if (ev.ContentDelta is { Length: > 0 } delta)
            {
                sb.Append(delta);
            }
        }

        return sb.ToString();
    }

    /// <summary>RF-005: provider context-overflow errors worth one compaction retry.</summary>
    private static bool IsContextLengthError(ChatProviderException error) =>
        error.StatusCode == 413
        || error.Message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("context length", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("maximum context", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("token limit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// P2 `/compact`: manual compaction — condenses stored history into a
    /// summary row reusable by later runs (no live wire required).
    /// </summary>
    public async Task<ChatConversationDto?> CompactConversationAsync(string id, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var provider = await GetProviderSnapshotAsync(conversation.ProviderId, ct).ConfigureAwait(false);
        if (provider is null)
        {
            throw new ChatValidationException("The conversation's provider no longer exists.");
        }

        var toolSet = await capabilities.ResolveToolSetAsync(ct).ConfigureAwait(false);
        var wire = new List<OpenAiChatMessage>();
        var state = new TurnState();
        if (!await SummarizePrefixAsync(provider, conversation, wire, toolSet, null, ct, ct).ConfigureAwait(false))
        {
            throw new ChatValidationException("Nothing to compact — history already fits the budget.");
        }

        return ToDto(conversation, preview: null);
    }



    // ---- SPEC-20261005-chat-tool-approval: the approval gate (RF-002..RF-005) ----

    /// <summary>
    /// Resolves the per-call policy and runs the ask path. Returns
    /// <c>null</c> when the call may execute, or the synthetic
    /// <c>(resultJson, refusalReason)</c> of a denied call (preset
    /// <c>chat</c>/<c>never</c> override, timeout <c>unavailable</c>, or a
    /// user rejection). <c>approval.asked</c>/<c>approval.decided</c> publish
    /// straight onto the broadcaster so waiting attached streams see the card
    /// immediately — the iterator only yields the tool result afterwards.
    /// <c>Refused=false</c> marks a synthetic result that is not a refusal
    /// (a rejected plan review — the model revises, nothing was denied).
    /// </summary>
    /// <summary>Outcome of the gate — null ResultJson = the call executes.</summary>
    private sealed record GateOutcome(
        string? ResultJson,
        string? RefusalReason,
        bool Abort,
        bool Refused,
        /// <summary>SPEC-20261013 RF-004: medium verdict to badge on the tool card.</summary>
        ChatToolRiskVerdict? Notice = null);

    /// <summary>Minimal context for the classifier — only WorkspacePath is read.</summary>
    private ChatToolContext ClassificationContext(
        ChatRun run, ChatConversation conversation, ChatProviderSnapshot provider) =>
        new(
            WorkspacePath: ResolveWorkspacePath(conversation),
            ProviderId: provider.Id,
            ProviderBaseUrl: provider.BaseUrl,
            ProviderApiKey: provider.ApiKey,
            ImageModel: string.Empty,
            SearchBackend: string.Empty,
            SearchUrl: string.Empty,
            SearchApiKey: string.Empty,
            ConversationId: conversation.Id.Value,
            Model: conversation.Model,
            RunId: run.Id.Value);

    /// <summary>The workspace the tools jail against — same resolution as the executor's context.</summary>
    private string ResolveWorkspacePath(ChatConversation conversation) =>
        // Agent-chat: an explicit workspace wins; otherwise the bound repo
        // resolves (null → the default ~/repos root).
        !string.IsNullOrWhiteSpace(conversation.WorkspacePath)
            ? conversation.WorkspacePath
            : workspace.ResolveCardWorkdir(conversation.RepositoryFullName, out _);

    private async Task<GateOutcome?> ApplyApprovalGateAsync(
        ChatRun run,
        ChatConversation conversation,
        ChatProviderSnapshot provider,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        OpenAiToolCall toolCall,
        CancellationToken ct,
        CancellationToken stoppingToken)
    {
        if (!toolSet.TryGetValue(toolCall.Name, out var tool))
        {
            return null; // unknown tool — the executor reports it refused.
        }

        // RF-006: mid-run preset/allowed-list changes take effect on the next
        // call — reload the two columns instead of trusting the tracked row.
        var fresh = await conversations.Query
            .AsNoTracking()
            .Where(c => c.Id == conversation.Id)
            .Select(c => new { c.PermissionPreset, c.AllowedToolsJson, c.PlanMode })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var preset = fresh?.PermissionPreset ?? conversation.PermissionPreset;
        var allowedTools = AllowedToolsOf(fresh?.AllowedToolsJson ?? conversation.AllowedToolsJson);
        var planMode = fresh?.PlanMode ?? conversation.PlanMode;

        // SPEC-20261005-chat-plan-mode RF-003: exit_plan_mode always asks —
        // a plan review is a semantic gate orthogonal to the tool preset
        // (open question #1). The call validates its plan argument first:
        // malformed plans fail the tool call without opening a review.
        if (string.Equals(toolCall.Name, ChatPlanMode.ToolName, StringComparison.Ordinal))
        {
            if (ValidatePlanArgument(toolCall.ArgumentsJson) is { } invalid)
            {
                return new GateOutcome(
                    JsonSerializer.Serialize(new { error = invalid }), "invalid plan", false, false);
            }

            return await AskAsync(run, conversation, toolCall, ChatApprovalKind.PlanReview, ct, stoppingToken)
                .ConfigureAwait(false);
        }

        var mutating = tool.RequiresConfirmation
            || ChatCapabilityRules.MutatingTools.Contains(toolCall.Name);

        // RF-002/RNF-001: plan mode is law — mutating calls are refused
        // regardless of the preset (enforcement at the gate, not the prompt).
        if (planMode == ChatPlanModes.On && mutating)
        {
            return new GateOutcome(JsonSerializer.Serialize(new { error = ChatPlanMode.MutatingDenial }),
                "denied by plan mode", false, true);
        }

        // SPEC-20261013 RF-004: the `auto` policy classifies the call —
        // low runs silent, medium runs with a notice + audit row, high asks.
        var resolution = ChatApprovalPolicy.ResolveDetailed(
            configuration, preset, allowedTools, toolCall.Name, mutating,
            tool.RequiresConfirmation,
            () => _riskClassifier.Classify(
                toolCall.Name, SafeArgs(toolCall.ArgumentsJson),
                ClassificationContext(run, conversation, provider)));

        switch (resolution.Decision)
        {
            case ChatApprovalDecision.Allow:
                // RF-004/RNF: medium auto-elevations get an audit row — the
                // decided approval (`auto:medium`) is the durable trail, the
                // risk.notice event badges the live tool card.
                if (resolution.Verdict is { Risk: ChatToolRisk.Medium } medium)
                {
                    var audit = ChatApproval.Create(
                        ChatApprovalId.NewGuid(), run.Id, conversation.Id,
                        toolCall.Id, toolCall.Name, toolCall.ArgumentsJson, UtcNow,
                        risk: medium.RiskValue, riskReason: medium.Reason);
                    audit.Decide(ChatApprovalStatus.AllowedOnce, "allow",
                        ChatApprovalDecidedBy.ForAutoRisk(medium.RiskValue), UtcNow);
                    await approvalRepository.AddAsync(audit, ct).ConfigureAwait(false);
                    await approvalRepository.SaveChangesAsync(ct).ConfigureAwait(false);
                    return new GateOutcome(null, null, false, false, medium);
                }

                return null;
            case ChatApprovalDecision.Deny:
                return new GateOutcome(JsonSerializer.Serialize(new
                {
                    error = $"Tool call blocked: '{toolCall.Name}' is denied by the conversation permission preset ({preset}).",
                }), "denied by permission policy", false, true);
        }

        return await AskAsync(run, conversation, toolCall, ChatApprovalKind.ToolCall, ct, stoppingToken,
            resolution.Verdict).ConfigureAwait(false);
    }

    /// <summary>Parses the call's args for classification — malformed JSON classifies as an empty object.</summary>
    private static JsonElement SafeArgs(string? argumentsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }

    /// <summary>
    /// The <c>exit_plan_mode</c> plan argument: non-empty markdown starting
    /// with '#' and within <see cref="ChatApproval.PlanPreviewMaxLength"/> so
    /// the persisted review row holds the whole plan (RF-003).
    /// </summary>
    private static string? ValidatePlanArgument(string? argumentsJson)
    {
        string? plan;
        try
        {
            plan = JsonSerializer.Deserialize<JsonElement>(
                    string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson)
                .TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        }
        catch (JsonException)
        {
            return "exit_plan_mode requires a valid 'plan' markdown argument.";
        }

        if (string.IsNullOrWhiteSpace(plan))
        {
            return "exit_plan_mode requires a non-empty 'plan' markdown argument.";
        }

        if (!plan.TrimStart().StartsWith('#'))
        {
            return "exit_plan_mode rejected: the plan must be markdown starting with a '#' title.";
        }

        if (plan.Length > ChatApproval.PlanPreviewMaxLength)
        {
            return $"exit_plan_mode rejected: the plan exceeds {ChatApproval.PlanPreviewMaxLength} chars — shorten it.";
        }

        return null;
    }

    /// <summary>
    /// The ask path shared by tool-call and plan-review approvals (RF-002):
    /// persist → broadcast <c>approval.asked</c> + notifier fan-out → suspend
    /// on the coordinator → resolve allow/deny/timeout/cancel.
    /// </summary>
    private async Task<GateOutcome?> AskAsync(
        ChatRun run, ChatConversation conversation, OpenAiToolCall toolCall,
        ChatApprovalKind kind, CancellationToken ct, CancellationToken stoppingToken,
        ChatToolRiskVerdict? risk = null)
    {
        var approval = ChatApproval.Create(
            ChatApprovalId.NewGuid(), run.Id, conversation.Id,
            toolCall.Id, toolCall.Name,
            kind == ChatApprovalKind.PlanReview ? ExtractPlan(toolCall.ArgumentsJson) : toolCall.ArgumentsJson,
            UtcNow, kind,
            risk?.RiskValue, risk?.Reason);

        await approvalRepository.AddAsync(approval, ct).ConfigureAwait(false);
        await approvalRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        broadcaster.Publish(run.Id.Value, new ChatApprovalAskedEvent(
            approval.Id.Value, toolCall.Id, toolCall.Name, approval.ArgumentsPreview, approval.Kind.Value,
            approval.Risk, approval.RiskReason));

        // RF-002: run.approval fan-out — same seam as run.completed; a closed
        // tab still learns the run needs a human. Best-effort per notifier.
        foreach (var notifier in notifiers)
        {
            try
            {
                await notifier.ApprovalAskedAsync(run, approval.Id.Value, toolCall.Name, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "chat approval {ApprovalId} notify failed", approval.Id.Value);
            }
        }

        var wait = approvalCoordinator.Register(approval.Id.Value);
        try
        {
            var timeoutSeconds = ChatApprovalPolicy.TimeoutSeconds(configuration);
            var timeout = timeoutSeconds > 0
                ? Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)
                : Task.Delay(Timeout.Infinite, ct);
            var completed = await Task.WhenAny(wait.Task, timeout).ConfigureAwait(false);

            if (completed == wait.Task)
            {
                // The decide endpoint already persisted the row and published
                // approval.decided — only the verdict is needed here.
                var verdict = await wait.Task.ConfigureAwait(false);
                if (verdict == ChatApprovalVerdict.Allowed)
                {
                    return null; // allowed-once — execute exactly this call.
                }

                var denied = await approvalRepository.Query
                    .AsNoTracking()
                    .Where(a => a.Id == approval.Id)
                    .Select(a => a.Decision)
                    .FirstOrDefaultAsync(CancellationToken.None).ConfigureAwait(false);

                // plan-review: the denial carries reviewer feedback — the
                // model revises in plan mode (RF-003), no refusal chip.
                if (kind == ChatApprovalKind.PlanReview)
                {
                    return new GateOutcome(JsonSerializer.Serialize(new
                    {
                        approved = false,
                        feedback = DenialFeedback(denied),
                    }), "plan review rejected", false, false);
                }

                return new GateOutcome(JsonSerializer.Serialize(new
                {
                    error = $"Tool call denied by the user{SuffixReason(denied)}",
                }), "denied by user", false, true);
            }

            // timeout or cancellation — distinguish by the run token.
            if (ct.IsCancellationRequested)
            {
                await CancelApprovalAsync(approval, stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested(); // host interrupt — propagate
                }
                // user stop: unwind the tool loop cleanly (Abort) — the run
                // ends through the normal stopped-by-user terminal path.
                return new GateOutcome(null, null, true, false);
            }

            await ExpireApprovalAsync(approval, stoppingToken).ConfigureAwait(false);
            if (kind == ChatApprovalKind.PlanReview)
            {
                // RNF-002 fail-closed: no answerer → stays in plan mode.
                return new GateOutcome(JsonSerializer.Serialize(new
                {
                    approved = false,
                    feedback = "Plan review timed out — still in plan mode.",
                }), "approval unavailable", false, false);
            }

            return new GateOutcome(JsonSerializer.Serialize(new
            {
                error = $"Tool call '{toolCall.Name}' could not run: approval request timed out (fail-closed).",
            }), "approval unavailable", false, true);
        }
        finally
        {
            approvalCoordinator.Unregister(approval.Id.Value);
        }
    }

    /// <summary>The 'deny: <feedback>' payload → reviewer feedback text.</summary>
    private static string DenialFeedback(string? decision)
    {
        const string deny = "deny:";
        return decision is not null && decision.StartsWith(deny, StringComparison.Ordinal)
            ? decision[deny.Length..].Trim()
            : decision ?? "rejected";
    }

    /// <summary>The <c>plan</c> argument of an exit_plan_mode call — the review row stores it raw.</summary>
    private static string ExtractPlan(string? argumentsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(
                    string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson)
                .TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlySet<string> AllowedToolsOf(string? allowedToolsJson)
    {
        if (string.IsNullOrWhiteSpace(allowedToolsJson))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(allowedToolsJson);
            return list is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static string SuffixReason(string? decision)
    {
        const string deny = "deny:";
        return decision is not null && decision.StartsWith(deny, StringComparison.Ordinal)
            ? $": {decision[deny.Length..].Trim()}"
            : ".";
    }

    /// <summary>auto-cancel: run stopped/shutdown while the approval was pending (RF-005).</summary>
    private async Task CancelApprovalAsync(ChatApproval approval, CancellationToken persistCt)
    {
        // Another decider (ui/timeout/stop endpoint) may have landed first in a
        // different scope — re-check the stored status so only the pending row
        // transitions (and audits) once.
        var stored = await approvalRepository.Query
            .AsNoTracking()
            .Where(a => a.Id == approval.Id)
            .Select(a => a.Status)
            .FirstOrDefaultAsync(CancellationToken.None).ConfigureAwait(false);
        if (stored is null || !stored.IsPending)
        {
            return;
        }

        await TransitionApprovalAsync(approval, a => a.Cancel(UtcNow), null).ConfigureAwait(false);
    }

    /// <summary>auto-timeout: no answerer inside the window → unavailable (fail-closed, RNF-002).</summary>
    private async Task ExpireApprovalAsync(ChatApproval approval, CancellationToken persistCt)
    {
        var stored = await approvalRepository.Query
            .AsNoTracking()
            .Where(a => a.Id == approval.Id)
            .Select(a => a.Status)
            .FirstOrDefaultAsync(CancellationToken.None).ConfigureAwait(false);
        if (stored is null || !stored.IsPending)
        {
            return;
        }

        await TransitionApprovalAsync(approval, a => a.Expire(UtcNow), null).ConfigureAwait(false);
    }

    /// <summary>
    /// Single atomic transition for a pending approval: mutate + audit note +
    /// one SaveChanges (so the note can never persist without the decision).
    /// A racing decider in another scope wins the Version race — on
    /// <see cref="DbUpdateConcurrencyException"/> this decider backs off
    /// silently: the winner already wrote row + note + broadcast.
    /// </summary>
    /// <param name="verdict">Resolved into the suspended gate, or null when the
    /// caller is the gate itself (its wait already ended).</param>
    private async Task TransitionApprovalAsync(
        ChatApproval approval, Action<ChatApproval> decide, ChatApprovalVerdict? verdict)
    {
        decide(approval);
        var note = BuildApprovalNote(approval);
        await messages.AddAsync(note, CancellationToken.None).ConfigureAwait(false);
        await IndexMessageSafeAsync(note, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await approvalRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another decider committed first — drop the stale row and the
            // staged audit note so later saves in this scope stay usable
            // (the winner already wrote row + note + broadcast).
            approvalRepository.Untrack(approval);
            messages.Untrack(note);
            return;
        }

        if (verdict is { } v)
        {
            approvalCoordinator.Resolve(approval.Id.Value, v);
        }
        broadcaster.Publish(approval.RunId.Value, new ChatApprovalDecidedEvent(
            approval.Id.Value, approval.Status.Value, approval.Decision,
            approval.DecidedBy!.Value, approval.Kind.Value));
    }

    /// <summary>RNF-003: every decision lands in the transcript as a system note row.</summary>
    private ChatMessage BuildApprovalNote(ChatApproval approval)
        => approval.Kind == ChatApprovalKind.PlanReview
            ? ChatMessage.CreateSystemNote(
                approval.ConversationId,
                $"Plan review {approval.Status.Value}: {approval.Decision} — {approval.DecidedBy?.Value}.",
                UtcNow)
            : ChatMessage.CreateSystemNote(
                approval.ConversationId,
                $"Tool approval {approval.Status.Value}: {approval.ToolName} ({approval.Decision}) — {approval.DecidedBy?.Value}.",
                UtcNow);

    /// <summary>
    /// RF-003: answers a pending approval — atomic (pending → decided only),
    /// remembered tools join the conversation allowed-list (RF-004). The
    /// <c>approval.decided</c> broadcast resolves the card on every attached
    /// stream and the coordinator wakes the suspended call.
    /// </summary>
    public async Task<ChatApprovalDto?> DecideApprovalAsync(
        string id, DecideChatApprovalRequest request, CancellationToken ct = default)
    {
        var approval = await approvalRepository.GetAsync(ChatApprovalId.From(id), ct).ConfigureAwait(false);
        if (approval is null)
        {
            return null;
        }

        var outcome = request.Outcome?.Trim().ToLowerInvariant();
        if (outcome is not ("allow" or "deny"))
        {
            throw new ChatValidationException("Outcome must be 'allow' or 'deny'.");
        }

        var allow = outcome == "allow";
        try
        {
            approval.DecideFromUi(allow, request.Reason, UtcNow);
        }
        catch (DomainException)
        {
            // Already decided (timeout, stop, or a racing answer) — 409.
            throw new ChatApprovalConflictException(
                $"Approval '{id}' is no longer pending (status: '{approval.Status.Value}').");
        }

        var isPlanReview = approval.Kind == ChatApprovalKind.PlanReview;
        ChatConversation? conversation = null;
        if (allow && request.RememberTool && !isPlanReview)
        {
            conversation = await conversations.GetAsync(approval.ConversationId, ct).ConfigureAwait(false);
            conversation?.AllowTool(approval.ToolName, UtcNow);
        }

        // RF-003: an approved plan review exits plan mode — the flip + the
        // 'plan mode off' note ride the same atomic flush as the decision.
        if (isPlanReview && allow)
        {
            conversation ??= await conversations.GetAsync(approval.ConversationId, ct).ConfigureAwait(false);
            if (conversation?.SetPlanMode(false, UtcNow) == true)
            {
                var offNote = ChatMessage.CreateSystemNote(conversation.Id, "plan mode off", UtcNow);
                await messages.AddAsync(offNote, ct).ConfigureAwait(false);
                await IndexMessageSafeAsync(offNote, ct).ConfigureAwait(false);
            }
        }

        // The audit note joins the same flush — it can never persist without
        // the decision (RNF-003).
        var auditNote = BuildApprovalNote(approval);
        await messages.AddAsync(auditNote, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(auditNote, ct).ConfigureAwait(false);
        try
        {
            await approvalRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A racing decide/timeout landed first — still 409 (RF-003).
            throw new ChatApprovalConflictException(
                $"Approval '{id}' was decided concurrently (status: '{approval.Status.Value}').");
        }

        approvalCoordinator.Resolve(
            approval.Id.Value,
            allow ? ChatApprovalVerdict.Allowed : ChatApprovalVerdict.Denied);
        broadcaster.Publish(approval.RunId.Value, new ChatApprovalDecidedEvent(
            approval.Id.Value, approval.Status.Value, approval.Decision,
            approval.DecidedBy!.Value, approval.Kind.Value));
        return ToDto(approval);
    }

    /// <summary>
    /// SPEC-20261005-chat-plan-mode RF-001: toggles plan mode mid-conversation
    /// — idempotent; writes the audit note and, on <c>on→off</c>, cancels any
    /// pending plan review (marked <c>cancelled</c>, auto-cancel audit) so the
    /// suspended <c>exit_plan_mode</c> call unblocks fail-closed.
    /// </summary>
    public async Task<ChatConversationDto?> SetPlanModeAsync(
        string id, bool active, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        if (!conversation.SetPlanMode(active, UtcNow))
        {
            return ToDto(conversation, null);
        }

        var planNote = ChatMessage.CreateSystemNote(
            conversation.Id, $"plan mode {(active ? "on" : "off")}", UtcNow);
        await messages.AddAsync(planNote, ct).ConfigureAwait(false);
        await IndexMessageSafeAsync(planNote, ct).ConfigureAwait(false);

        // on→off: a dangling review must not park the run — cancel it and
        // resolve the waiter Denied (the tool sees approved:false).
        if (!active)
        {
            var pendingReviews = await approvalRepository.Query
                .Where(a => a.ConversationId == conversation.Id
                    && a.Status == ChatApprovalStatus.Pending
                    && a.Kind == ChatApprovalKind.PlanReview)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var review in pendingReviews)
            {
                await TransitionApprovalAsync(review, a => a.Cancel(UtcNow), ChatApprovalVerdict.Denied)
                    .ConfigureAwait(false);
            }
        }

        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    /// <summary>RF-007: approvals of a conversation (re-attach replay) — optional status filter.</summary>
    public async Task<IReadOnlyList<ChatApprovalDto>> ListApprovalsAsync(
        string conversationId, string? status, CancellationToken ct = default)
    {
        var id = ChatConversationId.From(conversationId);
        var query = approvalRepository.Query
            .Where(a => a.ConversationId == id)
            .OrderByDescending(a => a.RequestedAt);
        var rows = string.IsNullOrWhiteSpace(status)
            ? await query.Take(100).ToListAsync(ct).ConfigureAwait(false)
            : await query.Where(a => a.Status == ChatApprovalStatus.From(status))
                .Take(100).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    private async Task<(string Json, bool Refused, string? Reason, IReadOnlyList<string>? ImageUrls)> ExecuteToolAsync(
        OpenAiToolCall toolCall, ChatProviderSnapshot provider,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatConversation conversation,
        ChatRun run,
        ChannelWriter<ChatStreamEvent> activity,
        CancellationToken ct)
    {
        if (!toolSet.TryGetValue(toolCall.Name, out var tool))
        {
            return (JsonSerializer.Serialize(new { error = $"unknown tool '{toolCall.Name}'" }), true, "unknown tool", null);
        }

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson);
        }
        catch (JsonException)
        {
            return (JsonSerializer.Serialize(new { error = "invalid tool arguments" }), true, "bad arguments", null);
        }

        var context = new ChatToolContext(
            // Agent-chat: an explicit workspace wins; otherwise the bound repo
            // resolves (null → the default ~/repos root).
            WorkspacePath: ResolveWorkspacePath(conversation),
            ProviderId: provider.Id,
            ProviderBaseUrl: provider.BaseUrl,
            ProviderApiKey: provider.ApiKey,
            ImageModel: ResolveImageModel(provider.Id),
            SearchBackend: configuration["Taskboard:Chat:SearchBackend"] ?? "none",
            SearchUrl: configuration["Taskboard:Chat:SearchUrl"] ?? string.Empty,
            SearchApiKey: configuration["Taskboard:Chat:SearchApiKey"] ?? string.Empty,
            ConversationId: conversation.Id.Value,
            Model: conversation.Model,
            DelegationDepth: 0,
            Activity: new ChannelActivityReporter(activity),
            ToolSet: toolSet,
            DefaultAgentCli: conversation.AgentCli,
            DefaultAgentModel: conversation.AgentModel,
            RunId: run.Id.Value);

        try
        {
            var result = await tool.ExecuteAsync(arguments, context, ct).ConfigureAwait(false);
            return (result.Json, result.Refused, result.RefusalReason, result.ImageDataUrls);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = ex.Message }), false, null, null);
        }
    }

    /// <summary>
    /// Transcript for a run = history up to its trigger message plus every
    /// non-user row after it — those are the tails of earlier runs that
    /// finished while this one sat queued. User messages persisted after the
    /// trigger belong to later queued runs and are skipped (RF-002).
    /// </summary>
    private async Task<List<OpenAiChatMessage>> BuildTranscriptAsync(
        ChatConversation conversation,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatMessageId? triggerMessageId,
        CancellationToken ct)
    {
        var rows = await messages.Query
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var trigger = triggerMessageId is null ? null : rows.FirstOrDefault(m => m.Id == triggerMessageId);

        // RF-002: the plan:policy section keys off the live value — a toggle
        // flipped while the run sat queued still lands on this transcript.
        var planMode = await conversations.Query
            .AsNoTracking()
            .Where(c => c.Id == conversation.Id)
            .Select(c => c.PlanMode)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? conversation.PlanMode;
        var wire = new List<OpenAiChatMessage>
        {
            new("system", SystemPrompt(
                toolSet, await SkillCatalogSectionAsync(toolSet, ct).ConfigureAwait(false), planMode)),
        };

        // SPEC-20261005-chat-attachments-feedback RF-002/RF-004: bound
        // attachments append descriptor lines to the wire text of their
        // message; image/* rows additionally emit image_url parts.
        var attachmentsByMessage = new Dictionary<string, List<ChatAttachment>>(StringComparer.Ordinal);
        if (attachmentRepository is not null)
        {
            var bound = await attachmentRepository.Query
                .AsNoTracking()
                .Where(a => a.ConversationId == conversation.Id && a.MessageId != null)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var attachment in bound)
            {
                if (!attachmentsByMessage.TryGetValue(attachment.MessageId!.Value, out var list))
                {
                    attachmentsByMessage[attachment.MessageId.Value] = list = [];
                }

                list.Add(attachment);
            }
        }

        // SPEC-20261005-chat-context-management RF-004/RNF-001: the latest
        // persisted summary supersedes the rows up to its bound on the wire;
        // the stored history itself is never rewritten.
        var latestSummary = rows.LastOrDefault(m => m.Kind == ChatMessageKinds.Summary);
        var supersededUntilIndex = -1;
        if (latestSummary?.SupersedesUntilMessageId is { } boundId)
        {
            supersededUntilIndex = rows.FindIndex(m => m.Id.Value == boundId);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var message = rows[i];
            if (i <= supersededUntilIndex)
            {
                continue; // condensed into the summary row below
            }

            if (trigger is not null
                && message.Role == ChatMessageRole.User
                && message.CreatedAt > trigger.CreatedAt)
            {
                continue;
            }

            var role = message.Role.Value;
            if (role == "system")
            {
                // Summary rows DO ride the wire (they are the compaction
                // payload); other system notes stay UI-only (RNF-003).
                if (message.Kind == ChatMessageKinds.Summary)
                {
                    wire.Add(new OpenAiChatMessage("system", message.Content));
                }

                continue;
            }

            if (role == "user")
            {
                if (attachmentsByMessage.TryGetValue(message.Id.Value, out var attachments) && attachments.Count > 0)
                {
                    wire.Add(new OpenAiChatMessage(
                        "user",
                        message.Content + DescribeAttachments(attachments),
                        ImageUrls: AttachmentImageDataUrls(attachments)));
                }
                else
                {
                    wire.Add(new OpenAiChatMessage("user", message.Content));
                }
            }
            else if (role == ChatMessageRole.Assistant.Value)
            {
                var toolCalls = message.ToolCallsJson is null
                    ? null
                    : JsonSerializer.Deserialize<List<OpenAiToolCall>>(message.ToolCallsJson, Json);
                wire.Add(new OpenAiChatMessage(ChatMessageRole.Assistant.Value, InlineToolCallMarkup.StripBlocks(message.Content), toolCalls));
            }
            else if (role == "tool" && message.ToolCallId is not null)
            {
                wire.Add(new OpenAiChatMessage("tool", message.Content, ToolCallId: message.ToolCallId, Name: message.ToolName));
            }
        }

        return wire;
    }

    private string SystemPrompt(
        IReadOnlyDictionary<string, IChatTool> toolSet, string skillCatalog, string? planMode = null)
    {
        var workdir = workspace.ResolveCardWorkdir(null, out _);
        var toolNames = string.Join(", ", toolSet.Keys.Order());
        // RNF-003: the section text is config-owned (Taskboard:Chat:PlanMode:Section).
        var planSection = planMode == ChatPlanModes.On
            ? $"\n{ChatPlanMode.Section(configuration)}"
            : string.Empty;
        return $"""
            You are the Harness Chat assistant running on the operator's host server.
            Current date: {UtcNow:yyyy-MM-dd}. Workspace directory: {workdir}.
            You can call tools to act on the host: {toolNames}. Commands and file access are
            confined to the workspace by a security gateway — dangerous operations are refused.
            Prefer tools when they answer the request; keep answers concise and use markdown.
            {skillCatalog}{planSection}
            """;
    }

    /// <summary>
    /// SPEC-20261001-chat-skills-slash-commands FR-002: enabled skills announced
    /// in the system prompt (≤40, description truncated) when use_skill is live.
    /// </summary>
    private async Task<string> SkillCatalogSectionAsync(
        IReadOnlyDictionary<string, IChatTool> toolSet, CancellationToken ct)
    {
        if (!toolSet.ContainsKey("use_skill"))
        {
            return string.Empty;
        }

        try
        {
            var discovered = await skills.DiscoverAsync(ct).ConfigureAwait(false);
            var lines = discovered
                .Where(sk => string.Equals(sk.Source, SkillDiscoverySource.Agents, StringComparison.OrdinalIgnoreCase))
                .Where(sk => capabilities.IsCapabilityEnabled($"skill:{sk.Name}"))
                .OrderBy(sk => sk.Name, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .Select(sk =>
                {
                    var desc = sk.Description is { Length: > 120 } ? string.Concat(sk.Description.AsSpan(0, 120), "…") : sk.Description;
                    return $"- {sk.Name} — {desc}";
                })
                .ToList();
            return lines.Count == 0
                ? string.Empty
                : "Available skills (invoke via use_skill or a user /command):\n" + string.Join('\n', lines);
        }
        catch (Exception)
        {
            return string.Empty; // discovery failure must never break the turn
        }
    }

    private static List<OpenAiToolDefinition> BuildToolDefinitions(IReadOnlyDictionary<string, IChatTool> toolSet)
    {
        return toolSet.Select(kv => new OpenAiToolDefinition(kv.Key, kv.Value.Description, kv.Value.ParametersJson))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private int ParseInt(string key, int defaultValue)
    {
        var raw = configuration[key];
        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }

    private long ParseLong(string key, long defaultValue)
    {
        var raw = configuration[key];
        return long.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }

    /// <summary>Fire-and-forget activity reporter over the turn's event channel.</summary>
    private sealed class ChannelActivityReporter(ChannelWriter<ChatStreamEvent> writer) : IChatActivityReporter
    {
        public void Report(string phase, string label) => writer.TryWrite(new ChatStatusEvent(phase, label));
    }

    /// <summary>Capability default values are "{providerId}:{model}" — the image
    /// model applies only when it belongs to the conversation's provider (RF-003/RF-009).</summary>
    private string ResolveImageModel(Guid providerId)
    {
        var raw = configuration["Taskboard:Chat:DefaultImageModel"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var separator = raw.IndexOf(':');
        return separator > 0
            && Guid.TryParse(raw[..separator], out var defaultProvider)
            && defaultProvider == providerId
            ? raw[(separator + 1)..]
            : string.Empty;
    }

    private bool ParseBool(string key, bool defaultValue)
    {
        var raw = configuration[key];
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private async Task<ChatProviderSnapshot> RequireProviderAsync(Guid providerId, CancellationToken ct)
    {
        var provider = await GetProviderSnapshotAsync(providerId, ct).ConfigureAwait(false);
        return provider ?? throw new ChatValidationException($"Provider '{providerId}' not found.");
    }

    // Per-id snapshot for hot paths (run ticks, model lists, enqueue checks)
    // that would otherwise hit the DB per call. L1-only via
    // DisableDistributedCache — the snapshot carries ApiKey and must never
    // reach a shared L2. Tagged ProvidersCacheTag so every provider
    // mutation's existing tag invalidation covers it.
    private async Task<ChatProviderSnapshot?> GetProviderSnapshotAsync(Guid providerId, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            ProviderKey(providerId),
            // AsNoTracking: the snapshot is a pure read model — a tracked
            // entity would also pin stale values inside this context.
            async inner => ToSnapshot(await providers.Query
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == providerId, inner)
                .ConfigureAwait(false)),
            new HybridCacheEntryOptions
            {
                Expiration = ProvidersTtl,
                Flags = HybridCacheEntryFlags.DisableDistributedCache,
            },
            [ProvidersCacheTag],
            ct).ConfigureAwait(false);
    }

    private static ChatProviderSnapshot? ToSnapshot(ChatProvider? provider) =>
        provider is null
            ? null
            : new ChatProviderSnapshot(provider.Id, provider.Name, provider.BaseUrl, provider.ApiKey, provider.Enabled);

    private static ChatProviderDto ToDto(ChatProvider provider) => new(
        provider.Id,
        provider.Name,
        provider.BaseUrl,
        provider.Enabled,
        HasApiKey: provider.ApiKey.Length > 0,
        KeyHint: provider.ApiKey.Length <= 4 ? "" : $"••••{provider.ApiKey[^4..]}",
        provider.CreatedAt,
        provider.UpdatedAt);

    private static ChatRunDto ToDto(ChatRun run) => new(
        run.Id.Value,
        run.ConversationId.Value,
        run.Status.Value,
        run.TriggerMessageId.Value,
        run.Error,
        run.TokensIn,
        run.TokensOut,
        run.CreatedAt,
        run.StartedAt,
        run.FinishedAt,
        run.ContextTokensLimit,
        run.CompactionCount,
        run.PausedAt);

    private static ChatApprovalDto ToDto(ChatApproval approval) => new(
        approval.Id.Value,
        approval.RunId.Value,
        approval.ConversationId.Value,
        approval.ToolCallId,
        approval.ToolName,
        approval.ArgumentsPreview,
        approval.Status.Value,
        approval.RequestedAt,
        approval.DecidedAt,
        approval.Decision,
        approval.DecidedBy?.Value,
        approval.Kind.Value,
        approval.Risk,
        approval.RiskReason);

    private static ChatConversationDto ToDto(
        ChatConversation conversation, string? preview, string? activeRunStatus = null,
        int forkCount = 0) => new(
        conversation.Id.Value,
        conversation.ProviderId,
        conversation.ProviderName,
        conversation.Model,
        conversation.Title,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        preview,
        conversation.AgentCli is null && conversation.RepositoryFullName is null
            && conversation.WorkspacePath is null && conversation.AgentModel is null
            ? null
            : new ChatAgentContext(
                conversation.AgentCli, conversation.RepositoryFullName,
                conversation.WorkspacePath, conversation.AgentModel),
        conversation.ArchivedAt,
        activeRunStatus,
        conversation.PermissionPreset,
        conversation.PlanMode,
        conversation.ForkedFromConversationId,
        conversation.ForkedAtMessageId,
        forkCount,
        conversation.PreviewUrl);

    private static ChatMessageDto ToDto(
        ChatMessage message,
        IReadOnlyList<ChatAttachment>? attachments = null,
        ChatMessageFeedback? feedback = null) => new(
        message.Id.Value,
        message.Role.Value,
        // Inline markup persisted before the stream filter existed still
        // renders as garbage — strip it at the DTO edge (SPEC-20261001-ai-chat-openwebui).
        message.Role == ChatMessageRole.Assistant ? InlineToolCallMarkup.StripBlocks(message.Content) : message.Content,
        message.ToolCallsJson,
        message.ToolCallId,
        message.ToolName,
        message.Refused,
        message.ImagePath,
        message.ImagePath is null ? null : $"/api/local/chat/images/{message.ImagePath}",
        message.TokensIn,
        message.TokensOut,
        message.Model,
        message.CreatedAt,
        message.Kind,
        attachments?.Select(ToDto).ToList(),
        feedback is null ? null : new ChatFeedbackDto(
            feedback.Rating, feedback.Category, feedback.Note, feedback.Version, feedback.UpdatedAt));

    private static ChatAttachmentDto ToDto(ChatAttachment attachment) => new(
        attachment.Id.Value,
        attachment.FileName,
        attachment.ContentType,
        attachment.ByteSize,
        $"/api/local/chat/conversations/{attachment.ConversationId.Value}/attachments/{attachment.Id.Value}/download");

    private static ChatDeliverableDto ToDto(ChatRunDeliverable deliverable) => new(
        deliverable.Path, deliverable.AddedLines, deliverable.RemovedLines,
        deliverable.Source, deliverable.Summary);

    /// <summary>
    /// RF-004/RF-005: one shot for the transcript DTOs — bound attachments +
    /// feedback rows joined by message id (attachments only for the visible
    /// window; feedback only on assistant rows).
    /// </summary>
    private async Task<List<ChatMessageDto>> EnrichMessagesAsync(
        IReadOnlyList<ChatMessage> rows, CancellationToken ct)
    {
        var messageIds = rows.Select(m => m.Id).ToList();
        var messageIdsNullable = rows.Select(m => (ChatMessageId?)m.Id).ToList();
        var attachmentsByMessage = new Dictionary<string, List<ChatAttachment>>(StringComparer.Ordinal);
        if (attachmentRepository is not null && messageIds.Count > 0)
        {
            var bound = await attachmentRepository.Query
                .AsNoTracking()
                .Where(a => a.MessageId != null && messageIdsNullable.Contains(a.MessageId))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var attachment in bound)
            {
                if (!attachmentsByMessage.TryGetValue(attachment.MessageId!.Value, out var list))
                {
                    attachmentsByMessage[attachment.MessageId.Value] = list = [];
                }

                list.Add(attachment);
            }
        }

        var feedbackByMessage = new Dictionary<string, ChatMessageFeedback>(StringComparer.Ordinal);
        if (feedbackRepository is not null && messageIds.Count > 0)
        {
            var feedbackRows = await feedbackRepository.Query
                .AsNoTracking()
                .Where(f => messageIds.Contains(f.MessageId))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var feedback in feedbackRows)
            {
                feedbackByMessage[feedback.MessageId.Value] = feedback;
            }
        }

        return rows.Select(m => ToDto(
            m,
            attachmentsByMessage.GetValueOrDefault(m.Id.Value),
            feedbackByMessage.GetValueOrDefault(m.Id.Value))).ToList();
    }
}

/// <summary>Serializable per-id provider snapshot cached in HybridCache L1
/// (SPEC cache-flow audit): the entity itself can't ride the cache (no
/// parameterless ctor, and L1 clones via the serializer anyway). L1-only via
/// DisableDistributedCache — carries <c>ApiKey</c>, must never reach a shared
/// L2.</summary>
internal sealed record ChatProviderSnapshot(Guid Id, string Name, string BaseUrl, string ApiKey, bool Enabled);
