using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// A built-in chat tool exposed to the model via OpenAI function calling
/// (SPEC-20260929-ai-code-provider-chat RF-006). Implementations are
/// auto-confined: the security gateway, path jail and secret scrubbing apply
/// inside <see cref="ExecuteAsync"/> — never per-call approval.
/// </summary>
public interface IChatTool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON schema for the <c>parameters</c> field of the OpenAI tool definition.</summary>
    string ParametersJson { get; }

    Task<ChatToolResult> ExecuteAsync(JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken);
}

public sealed record ChatToolResult(string Json, bool Refused = false, string? RefusalReason = null);

/// <summary>Per-execution context handed to every tool.</summary>
public sealed record ChatToolContext(
    string WorkspacePath,
    Guid ProviderId,
    string ProviderBaseUrl,
    string ProviderApiKey,
    string ImageModel,
    string SearchBackend,
    string SearchUrl,
    string SearchApiKey);

/// <summary>Web search backend behind the <c>web_search</c> tool (RF-007).</summary>
public interface ISearchBackend
{
    Task<IReadOnlyList<ChatSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

public sealed record ChatSearchResult(string Title, string Url, string Snippet);
