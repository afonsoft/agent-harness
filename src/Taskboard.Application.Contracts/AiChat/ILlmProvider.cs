namespace Taskboard.Application.Contracts.AiChat;

public interface ILlmProvider
{
    string ModelId { get; }

    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed record LlmMessage(
    string Role,
    string Content,
    string? Name = null);

public sealed record LlmOptions(
    double? Temperature = null,
    int? MaxTokens = null,
    double? TopP = null,
    IReadOnlyList<string>? StopSequences = null);

public sealed record LlmResponse(
    string Content,
    LlmUsage? Usage = null,
    string? FinishReason = null);

public sealed record LlmUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

public sealed record LlmStreamChunk(
    string? ContentDelta = null,
    bool IsComplete = false,
    LlmUsage? Usage = null);