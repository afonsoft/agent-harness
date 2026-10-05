using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Application.Contracts.Workspace;
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

/// <summary>Request-level validation failure surfaced as 400.</summary>
public sealed class ChatValidationException(string message) : Exception(message);

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
    TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    // SPEC-20261004-provider-pick-hybridcache RF-002: chat catalog caching via
    // HybridCache (L1 memory; L2 = Redis when Taskboard:Cache:Redis is set).
    internal const string ProvidersCacheKey = "chat-providers";
    internal const string ProvidersCacheTag = "chat-providers";
    internal const string ProviderModelsCacheTag = "chat-provider-models";
    internal static readonly TimeSpan ProvidersTtl = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ProviderModelsTtl = TimeSpan.FromMinutes(5);

    private static string ProviderModelsKey(Guid providerId) => $"chat-provider-models-{providerId}";

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
        string? query, CancellationToken ct = default)
    {
        var rows = await conversations.Query
            .OrderByDescending(c => c.UpdatedAt)
            .Take(200)
            .ToListAsync(ct).ConfigureAwait(false);
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

        return rows.Select(c => ToDto(c, previewByConversation.GetValueOrDefault(c.Id.Value))).ToList();
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
        return new ChatConversationDetailDto(ToDto(conversation, null), rows.Select(ToDto).ToList());
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

        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(conversation, null);
    }

    public async Task<bool> DeleteConversationAsync(string id, CancellationToken ct = default)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(id), ct).ConfigureAwait(false);
        if (conversation is null)
        {
            return false;
        }

        await conversations.DeleteAsync(conversation, ct).ConfigureAwait(false);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ---- Send / stream / stop (RF-005) ----

    public async Task<IAsyncEnumerable<ChatStreamEvent>> SendMessageAsync(
        string conversationId, string content, CancellationToken requestAborted)
    {
        var conversation = await conversations.GetAsync(ChatConversationId.From(conversationId), requestAborted).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");
        var provider = await providers.GetAsync(conversation.ProviderId, requestAborted).ConfigureAwait(false)
            ?? throw new ChatValidationException($"Provider '{conversation.ProviderName}' no longer exists.");

        // B-01: the coordinator is a singleton — /stop from another request
        // (another scoped ChatService) reaches this run's token.
        var cts = await runs.BeginAsync(conversation.Id.Value, requestAborted).ConfigureAwait(false);
        return StreamTurnAsync(conversation, provider, content, cts, requestAborted);
    }

    public bool Stop(string conversationId) => runs.Stop(conversationId);

    private async IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(
        ChatConversation conversation,
        ChatProvider provider,
        string content,
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        var ct = cts.Token;
        var userMessage = ChatMessage.CreateUser(conversation.Id, content, UtcNow);
        await messages.AddAsync(userMessage, ct).ConfigureAwait(false);
        conversation.EnsureTitle(content, UtcNow);
        conversation.Touch(UtcNow);
        await conversations.SaveChangesAsync(ct).ConfigureAwait(false);

        // SPEC-20261001-chat-capability-registry FR-003: effective tool set —
        // disabled capabilities never reach the provider payload.
        var toolSet = await capabilities.ResolveToolSetAsync(ct).ConfigureAwait(false);
        var wire = await BuildTranscriptAsync(conversation, toolSet, ct).ConfigureAwait(false);
        var toolDefs = BuildToolDefinitions(toolSet);
        var state = new TurnState();
        var maxIterations = ParseInt("Taskboard:Chat:MaxToolIterations", 8);
        // Reasoning models burn output tokens on reasoning_content before the
        // answer — without an explicit budget gateways cap too low and the
        // turn ends with empty content.
        var maxTokens = ParseInt("Taskboard:Chat:MaxTokens", 4096);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // B-02: deltas are yielded as they arrive — the SSE client sees
            // live progress instead of a burst after the provider finishes.
            var stream = new StreamOutcome();
            var consume = new ConsumeContext(
                provider, conversation, wire, toolDefs, maxTokens, stream, cts, requestAborted);
            await foreach (var ev in ConsumeStreamAsync(consume, ct).ConfigureAwait(false))
            {
                yield return ev;
            }

            UpdateState(stream, state);
            if (stream.ProviderError is not null)
            {
                break;
            }

            // Flush the inline-markup filter tail — held-back marker prefixes
            // resolve here (visible) or are dropped (partial markup).
            if (stream.Complete() is { Length: > 0 } tail)
            {
                yield return new ChatDeltaEvent(tail);
            }

            // After a user stop the run token is cancelled — the partial reply
            // is still persisted using the request token, which stays alive.
            var persistCt = stream.StoppedByUser ? requestAborted : ct;
            var toolCalls = await PersistAssistantTurnAsync(
                    conversation, wire, stream, state.TokensIn, state.TokensOut, persistCt)
                .ConfigureAwait(false);

            if (toolCalls.Count == 0)
            {
                break;
            }

            await foreach (var ev in RunToolCallsAsync(toolCalls, provider, toolSet, conversation, wire, ct)
                .ConfigureAwait(false))
            {
                yield return ev;
            }

            conversation.Touch(UtcNow);
            await conversations.SaveChangesAsync(persistCt).ConfigureAwait(false);
        }

        await conversations.SaveChangesAsync(requestAborted.IsCancellationRequested ? CancellationToken.None : requestAborted)
            .ConfigureAwait(false);
        runs.End(conversation.Id.Value, cts);
        yield return new ChatDoneEvent(state.TokensIn, state.TokensOut, state.Error is null ? "stop" : "error", state.Error);
    }

    /// <summary>Mutable per-turn accumulators carried across tool-call iterations.</summary>
    private sealed class TurnState
    {
        public int? TokensIn { get; set; }

        public int? TokensOut { get; set; }

        public string? Error { get; set; }
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
        ChatProvider Provider,
        ChatConversation Conversation,
        List<OpenAiChatMessage> Wire,
        List<OpenAiToolDefinition> ToolDefs,
        int MaxTokens,
        StreamOutcome Outcome,
        CancellationTokenSource Stop,
        CancellationToken RequestAborted);

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

            if (!await TryMoveNextAsync(next, ctx.Outcome, ctx.Stop, ctx.RequestAborted).ConfigureAwait(false))
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
        CancellationToken requestAborted)
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
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            outcome.StoppedByUser = true;
        }

        return false;
    }

    private async IAsyncEnumerable<ChatStreamEvent> RunToolCallsAsync(
        List<OpenAiToolCall> toolCalls,
        ChatProvider provider,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var toolCall in toolCalls)
        {
            // FR-003: live status — the "running" event reaches the client
            // before the (possibly long) tool call completes; tool-reported
            // activity is drained from a channel while it executes.
            var activity = Channel.CreateUnbounded<ChatStreamEvent>();
            yield return new ChatStatusEvent("running_tool", toolCall.Name);
            var toolTask = ExecuteToolAsync(toolCall, provider, toolSet, conversation, activity.Writer, ct);
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

            var (resultJson, refused, refusalReason) = await toolTask.ConfigureAwait(false);
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
            await messages.SaveChangesAsync(ct).ConfigureAwait(false);
            wire.Add(new OpenAiChatMessage("tool", resultJson, ToolCallId: toolCall.Id, Name: toolCall.Name));
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

    private async Task<(string Json, bool Refused, string? Reason)> ExecuteToolAsync(
        OpenAiToolCall toolCall, ChatProvider provider,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatConversation conversation,
        ChannelWriter<ChatStreamEvent> activity,
        CancellationToken ct)
    {
        if (!toolSet.TryGetValue(toolCall.Name, out var tool))
        {
            return (JsonSerializer.Serialize(new { error = $"unknown tool '{toolCall.Name}'" }), true, "unknown tool");
        }

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson);
        }
        catch (JsonException)
        {
            return (JsonSerializer.Serialize(new { error = "invalid tool arguments" }), true, "bad arguments");
        }

        var context = new ChatToolContext(
            // Agent-chat: an explicit workspace wins; otherwise the bound repo
            // resolves (null → the default ~/repos root).
            WorkspacePath: !string.IsNullOrWhiteSpace(conversation.WorkspacePath)
                ? conversation.WorkspacePath
                : workspace.ResolveCardWorkdir(conversation.RepositoryFullName, out _),
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
            DefaultAgentModel: conversation.AgentModel);

        try
        {
            var result = await tool.ExecuteAsync(arguments, context, ct).ConfigureAwait(false);
            return (result.Json, result.Refused, result.RefusalReason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = ex.Message }), false, null);
        }
    }

    private async Task<List<OpenAiChatMessage>> BuildTranscriptAsync(
        ChatConversation conversation,
        IReadOnlyDictionary<string, IChatTool> toolSet, CancellationToken ct)
    {
        var rows = await messages.Query
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var wire = new List<OpenAiChatMessage>
        {
            new("system", SystemPrompt(toolSet, await SkillCatalogSectionAsync(toolSet, ct).ConfigureAwait(false))),
        };
        foreach (var message in rows)
        {
            var role = message.Role.Value;
            if (role == "user")
            {
                wire.Add(new OpenAiChatMessage("user", message.Content));
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

    private string SystemPrompt(IReadOnlyDictionary<string, IChatTool> toolSet, string skillCatalog)
    {
        var workdir = workspace.ResolveCardWorkdir(null, out _);
        var toolNames = string.Join(", ", toolSet.Keys.Order());
        return $"""
            You are the Harness Chat assistant running on the operator's host server.
            Current date: {UtcNow:yyyy-MM-dd}. Workspace directory: {workdir}.
            You can call tools to act on the host: {toolNames}. Commands and file access are
            confined to the workspace by a security gateway — dangerous operations are refused.
            Prefer tools when they answer the request; keep answers concise and use markdown.
            {skillCatalog}
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

    private async Task<ChatProvider> RequireProviderAsync(Guid providerId, CancellationToken ct)
    {
        var provider = await providers.GetAsync(providerId, ct).ConfigureAwait(false);
        return provider ?? throw new ChatValidationException($"Provider '{providerId}' not found.");
    }

    private static ChatProviderDto ToDto(ChatProvider provider) => new(
        provider.Id,
        provider.Name,
        provider.BaseUrl,
        provider.Enabled,
        HasApiKey: provider.ApiKey.Length > 0,
        KeyHint: provider.ApiKey.Length <= 4 ? "" : $"••••{provider.ApiKey[^4..]}",
        provider.CreatedAt,
        provider.UpdatedAt);

    private static ChatConversationDto ToDto(ChatConversation conversation, string? preview) => new(
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
                conversation.WorkspacePath, conversation.AgentModel));

    private static ChatMessageDto ToDto(ChatMessage message) => new(
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
        message.CreatedAt);

}
