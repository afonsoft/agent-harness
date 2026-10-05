using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261005-chat-jobs-schedule-search RF-010: read-only full-text search
/// over persisted chat messages — the model's way to recall earlier turns
/// across conversations. The index is scoped (EF/raw SQLite) so the singleton
/// tool resolves it per call.
/// </summary>
public sealed class SessionSearchTool(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IChatTool
{
    private const int MaxResults = 10;
    private const int MaxSnippetChars = 200;

    public string Name => "session_search";
    public string Description =>
        "Full-text search across chat history. Returns up to 10 ranked hits "
        + "with short snippets; pass conversation_id to restrict to this conversation.";
    public string ParametersJson =>
        """{"type":"object","properties":{"query":{"type":"string","description":"Free-text terms (all must match)"},"conversation_id":{"type":"string","description":"Restrict to one conversation (default: all)"},"limit":{"type":"integer","description":"Max hits, cap 10"}},"required":["query"]}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(q.GetString()))
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = "query is required" }),
                Refused: true, "empty query");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.SearchEnabledKey))
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = "chat search is disabled" }),
                Refused: true, "search disabled");
        }

        var conversationId = arguments.TryGetProperty("conversation_id", out var cid)
            && cid.ValueKind == JsonValueKind.String
                ? cid.GetString()
                : context.ConversationId;
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lv)
            ? Math.Clamp(lv, 1, MaxResults)
            : MaxResults;

        await using var scope = scopeFactory.CreateAsyncScope();
        var index = scope.ServiceProvider.GetRequiredService<IChatMessageSearchIndex>();
        var hits = await index.SearchAsync(q.GetString() ?? string.Empty, conversationId, limit, cancellationToken)
            .ConfigureAwait(false);
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            hits = hits.Select(h => new
            {
                conversationId = h.ConversationId,
                messageId = h.MessageId,
                snippet = h.Snippet.Length <= MaxSnippetChars
                    ? h.Snippet
                    : h.Snippet[..MaxSnippetChars],
                createdAt = h.CreatedAt,
            }).ToList(),
        }));
    }
}
