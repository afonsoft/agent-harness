using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261016-chat-browser-tool RF-003: browser screenshots as
/// <see cref="ChatAttachment"/> rows prefixed <c>browser-shot-</c>; a
/// conversation keeps at most <see cref="MaxShotsPerConversation"/> shots —
/// the oldest is dropped (row + file) when the cap is exceeded.
/// </summary>
public sealed class ChatShotStore(
    IRepository<ChatAttachment> attachments,
    ChatAttachmentStore byteStore) : IChatShotStore
{
    /// <summary>Filename prefix marking browser shots (the "tag").</summary>
    public const string Prefix = "browser-shot-";

    /// <summary>RF-003: gallery cap per conversation.</summary>
    public const int MaxShotsPerConversation = 25;

    public async Task<string> SaveShotAsync(
        string conversationId, string action, byte[] png, CancellationToken cancellationToken)
    {
        var convId = ChatConversationId.From(conversationId);
        var id = ChatAttachmentId.NewGuid();
        var fileName = $"{Prefix}{DateTime.UtcNow:yyyyMMddHHmmss}-{action}.png";
        var (storagePath, sha256) = byteStore.Save(id.Value, "image/png", png);

        var row = ChatAttachment.Create(
            id, convId, fileName, "image/png", png.LongLength, storagePath, sha256, DateTime.UtcNow);
        await attachments.AddAsync(row, cancellationToken).ConfigureAwait(false);

        // Cap: with this shot the conversation may hold at most MaxShots — the
        // stale tail (row + bytes) goes away.
        var shots = await ShotRowsAsync(conversationId, cancellationToken).ConfigureAwait(false);
        foreach (var stale in shots.Skip(MaxShotsPerConversation - 1))
        {
            byteStore.Delete(stale.StoragePath);
            await attachments.DeleteAsync(stale, cancellationToken).ConfigureAwait(false);
        }

        await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id.Value;
    }

    public async Task<IReadOnlyList<BrowserShotDto>> ListShotsAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        var shots = await ShotRowsAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return shots
            .Select(a => new BrowserShotDto(
                a.Id.Value, a.FileName,
                $"/api/local/chat/conversations/{conversationId}/attachments/{a.Id.Value}/download",
                a.CreatedAt))
            .ToList();
    }

    private async Task<List<ChatAttachment>> ShotRowsAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        var convId = ChatConversationId.From(conversationId);
        return await attachments.Query
            .Where(a => a.ConversationId == convId
                && a.FileName.StartsWith(Prefix)
                && a.ContentType == "image/png")
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
