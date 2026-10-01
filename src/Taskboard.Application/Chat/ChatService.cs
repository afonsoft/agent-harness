using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;
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
    TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    // ---- Providers (RF-001/RF-002) ----

    public async Task<IReadOnlyList<ChatProviderDto>> ListProvidersAsync(CancellationToken ct = default)
    {
        var rows = await providers.Query.OrderBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
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
        return true;
    }

    public async Task<ChatModelListDto> ListModelsAsync(Guid providerId, CancellationToken ct = default)
    {
        var provider = await RequireProviderAsync(providerId, ct).ConfigureAwait(false);
        try
        {
            var models = await client.ListModelsAsync(provider.BaseUrl, provider.ApiKey, ct).ConfigureAwait(false);
            return new ChatModelListDto(models, Cached: false);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatProviderException($"Provider unreachable: {ex.Message}", 502);
        }
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
        var wire = await BuildTranscriptAsync(conversation, provider, toolSet, ct).ConfigureAwait(false);
        var toolDefs = BuildToolDefinitions(toolSet);
        int? tokensIn = null;
        int? tokensOut = null;
        string? error = null;
        var maxIterations = ParseInt("Taskboard:Chat:MaxToolIterations", 8);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // B-02: deltas are yielded as they arrive — the SSE client sees
            // live progress instead of a burst after the provider finishes.
            var stream = new StreamOutcome();
            await foreach (var ev in ConsumeStreamAsync(
                    provider, conversation, wire, toolDefs, stream, cts, requestAborted, ct)
                .ConfigureAwait(false))
            {
                yield return ev;
            }

            if (stream.Usage is { } usage)
            {
                tokensIn = usage.PromptTokens ?? tokensIn;
                tokensOut = usage.CompletionTokens ?? tokensOut;
            }

            if (stream.StoppedByUser)
            {
                error = "stopped by user";
            }

            if (stream.ProviderError is not null)
            {
                error = stream.ProviderError.Message;
                break;
            }

            var toolCalls = stream.MaterializeToolCalls();

            // After a user stop the run token is cancelled — the partial reply
            // is still persisted using the request token, which stays alive.
            var persistCt = stream.StoppedByUser ? requestAborted : ct;
            var assistantMessage = ChatMessage.CreateAssistant(
                conversation.Id, stream.AssistantContent.ToString(),
                toolCalls.Count > 0
                    ? JsonSerializer.Serialize(toolCalls.Select(tc => new { id = tc.Id, name = tc.Name, arguments = tc.ArgumentsJson }).ToList())
                    : null,
                tokensIn, tokensOut, conversation.Model, UtcNow);
            await messages.AddAsync(assistantMessage, persistCt).ConfigureAwait(false);
            await messages.SaveChangesAsync(persistCt).ConfigureAwait(false);

            wire.Add(new OpenAiChatMessage("assistant", stream.AssistantContent.ToString(), toolCalls));

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
        yield return new ChatDoneEvent(tokensIn, tokensOut, error is null ? "stop" : "error", error);
    }

    // Accumulated output of one provider stream pass: assistant text,
    // tool-call fragments, usage, and the terminal failure. Deltas are
    // streamed live by ConsumeStreamAsync (B-02), not replayed from here.
    private sealed class StreamOutcome
    {
        public System.Text.StringBuilder AssistantContent { get; } = new();

        public SortedDictionary<int, (string? Id, string? Name, System.Text.StringBuilder Args)> ToolAccumulator { get; } = new();

        public OpenAiUsage? Usage { get; private set; }

        public ChatProviderException? ProviderError { get; set; }

        public bool StoppedByUser { get; set; }

        /// <summary>Returns the content delta when the chunk carried one.</summary>
        public string? AccumulateChunk(OpenAiStreamEvent chunk)
        {
            var delta = chunk.ContentDelta is { Length: > 0 } content ? content : null;
            if (delta is not null)
            {
                AssistantContent.Append(delta);
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

            return delta;
        }

        public List<OpenAiToolCall> MaterializeToolCalls() =>
            ToolAccumulator
                .Select(kv => new OpenAiToolCall(
                    Id: kv.Value.Id ?? $"call_{kv.Key}",
                    Name: kv.Value.Name ?? "unknown",
                    ArgumentsJson: string.IsNullOrWhiteSpace(kv.Value.Args.ToString()) ? "{}" : kv.Value.Args.ToString()))
                .ToList();
    }

    // B-02: streams provider chunks as they arrive — each content delta is
    // yielded immediately and a keepalive ChatStatusEvent("streaming") is
    // emitted when the provider goes quiet, so the SSE connection never idles.
    private async IAsyncEnumerable<ChatStreamEvent> ConsumeStreamAsync(
        ChatProvider provider,
        ChatConversation conversation,
        List<OpenAiChatMessage> wire,
        List<OpenAiToolDefinition> toolDefs,
        StreamOutcome outcome,
        CancellationTokenSource cts,
        CancellationToken requestAborted,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var heartbeat = TimeSpan.FromSeconds(
            ParseInt("Taskboard:Chat:SseHeartbeatSeconds", 15));
        await using var enumerator = client.StreamChatAsync(
            provider.BaseUrl, provider.ApiKey, conversation.Model, wire,
            toolDefs.Count > 0 ? toolDefs : null, ct).GetAsyncEnumerator(ct);

        while (true)
        {
            // Heartbeat loop runs without a catch around the yield — a cancelled
            // delay breaks out and the real exception surfaces on `await next`.
            var next = enumerator.MoveNextAsync().AsTask();
            while (!next.IsCompleted)
            {
                var completed = await Task.WhenAny(next, Task.Delay(heartbeat, ct)).ConfigureAwait(false);
                if (completed == next || ct.IsCancellationRequested)
                {
                    break;
                }

                yield return new ChatStatusEvent("streaming", null);
            }

            bool moved;
            try
            {
                moved = await next.ConfigureAwait(false);
            }
            catch (ChatProviderException ex)
            {
                outcome.ProviderError = ex;
                yield break;
            }
            catch (HttpRequestException ex)
            {
                outcome.ProviderError = new ChatProviderException($"Provider unreachable: {ex.Message}", 502);
                yield break;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !requestAborted.IsCancellationRequested)
            {
                outcome.StoppedByUser = true;
                yield break;
            }

            if (!moved)
            {
                yield break;
            }

            if (outcome.AccumulateChunk(enumerator.Current) is { } delta)
            {
                yield return new ChatDeltaEvent(delta);
            }
        }
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

                await Task.Delay(150).ConfigureAwait(false);
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
            await messages.AddAsync(toolMessage, ct).ConfigureAwait(false);
            await messages.SaveChangesAsync(ct).ConfigureAwait(false);
            wire.Add(new OpenAiChatMessage("tool", resultJson, ToolCallId: toolCall.Id, Name: toolCall.Name));
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
            WorkspacePath: workspace.ResolveCardWorkdir(null, out _),
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
            ToolSet: toolSet);

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
        ChatConversation conversation, ChatProvider provider,
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
            else if (role == "assistant")
            {
                var toolCalls = message.ToolCallsJson is null
                    ? null
                    : JsonSerializer.Deserialize<List<OpenAiToolCall>>(message.ToolCallsJson, Json);
                wire.Add(new OpenAiChatMessage("assistant", message.Content, toolCalls));
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
        preview);

    private static ChatMessageDto ToDto(ChatMessage message) => new(
        message.Id.Value,
        message.Role.Value,
        message.Content,
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

    private static System.Text.StringBuilder Append(System.Text.StringBuilder builder, string? value)
    {
        builder.Append(value ?? string.Empty);
        return builder;
    }
}
