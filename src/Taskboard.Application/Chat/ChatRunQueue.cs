using System.Threading.Channels;

namespace Taskboard.Application.Chat;

/// <summary>Work item handed to the chat run dispatcher (SPEC-20261005-chat-background-resume RF-002).</summary>
public sealed record ChatRunWorkItem(string RunId, string ConversationId);

/// <summary>
/// SPEC-20261005-chat-background-resume RF-002: process-wide queue between the
/// enqueue endpoint (any scoped <see cref="ChatService"/>) and the hosted
/// <c>ChatRunDispatcherService</c>. Unbounded, single reader — per-conversation
/// FIFO is enforced by the dispatcher's per-conversation task chain.
/// </summary>
public sealed class ChatRunQueue
{
    private readonly Channel<ChatRunWorkItem> _channel =
        Channel.CreateUnbounded<ChatRunWorkItem>();

    public void Enqueue(ChatRunWorkItem item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<ChatRunWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
