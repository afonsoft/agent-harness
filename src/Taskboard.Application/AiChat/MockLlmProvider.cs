using Taskboard.Application.Contracts.AiChat;

namespace Taskboard.Application.AiChat;

public sealed class MockLlmProvider : ILlmProvider
{
    public string ModelId => "mock";

    public Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

        return Task.FromResult(new LlmResponse(
            Content: $"Mock response to: {lastUserMessage}",
            Usage: new LlmUsage(10, 20, 30),
            FinishReason: "stop"));
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var response = $"Mock streaming response to: {lastUserMessage}";

        foreach (var word in response.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            yield return new LlmStreamChunk(ContentDelta: word + " ");
        }

        yield return new LlmStreamChunk(
            ContentDelta: null,
            IsComplete: true,
            Usage: new LlmUsage(10, 20, 30));
    }
}