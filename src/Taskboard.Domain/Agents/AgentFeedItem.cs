using Taskboard;
using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-001: one persistent entry of the
/// global agents feed — produced on attention transitions, mailbox lifecycle,
/// dispatch lifecycle, worktree and PR events. <see cref="Body"/> is capped at
/// 280 chars at the producer (never at the client).
/// </summary>
public sealed class AgentFeedItem : Entity<string>
{
    public const int BodyMaxLength = 280;
    public const int TitleMaxLength = 200;
    public const int DeepLinkMaxLength = 512;

    private AgentFeedItem(string id)
        : base(id)
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public AgentFeedKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string DeepLink { get; private set; } = string.Empty;
    public string? SessionKey { get; private set; }
    public string? Cli { get; private set; }
    public int? IssueNumber { get; private set; }
    public DateTime AtUtc { get; private set; }
    public bool NeedsAttention { get; private set; }
    /// <summary>RF-008: dedup merge window hits — bumps AtUtc + this counter.</summary>
    public int RepeatCount { get; private set; }

    public static AgentFeedItem Create(
        string userId, AgentFeedKind kind, string title, string? body,
        string deepLink, string? sessionKey, string? cli, int? issueNumber,
        bool needsAttention, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "userId is required");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "title is required");
        }

        return new AgentFeedItem($"feed-{Guid.NewGuid():N}")
        {
            UserId = userId.Trim(),
            Kind = kind,
            Title = title.Trim().Length > TitleMaxLength
                ? title.Trim()[..TitleMaxLength]
                : title.Trim(),
            Body = string.IsNullOrWhiteSpace(body)
                ? null
                : (body.Trim().Length > BodyMaxLength ? body.Trim()[..BodyMaxLength] : body.Trim()),
            DeepLink = string.IsNullOrWhiteSpace(deepLink)
                ? "/agents?tab=dashboard"
                : deepLink.Trim(),
            SessionKey = string.IsNullOrWhiteSpace(sessionKey) ? null : sessionKey.Trim(),
            Cli = string.IsNullOrWhiteSpace(cli) ? null : cli.Trim(),
            IssueNumber = issueNumber,
            AtUtc = atUtc,
            NeedsAttention = needsAttention,
        };
    }

    /// <summary>RF-008: dedup window hit — refresh AtUtc and count the repeat.</summary>
    public void MergeRepeat(DateTime atUtc)
    {
        if (atUtc > AtUtc)
        {
            AtUtc = atUtc;
        }

        RepeatCount++;
    }
}
