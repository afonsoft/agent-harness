namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agent-board-v2 RF-006: per-user "read" stamp on a board card.
/// Key = "<c>userId|cardId</c>"; unread = card previewAtUtc &gt; ReadAtUtc (or
/// no row). Shared by the feed's unread model (same rule).
/// </summary>
public sealed class AgentBoardReadMarker : Entity<string>
{
    private AgentBoardReadMarker(string id)
        : base(id)
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public string CardId { get; private set; } = string.Empty;
    public DateTime ReadAtUtc { get; private set; }

    public static string KeyOf(string userId, string cardId) => $"{userId}|{cardId}";

    public static AgentBoardReadMarker Create(string userId, string cardId, DateTime atUtc) =>
        new(KeyOf(userId, cardId))
        {
            UserId = userId,
            CardId = cardId,
            ReadAtUtc = atUtc,
        };

    public void Touch(DateTime atUtc)
    {
        if (atUtc > ReadAtUtc)
        {
            ReadAtUtc = atUtc;
        }
    }
}
