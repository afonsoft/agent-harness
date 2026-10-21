namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-005: per-user CLI visibility on
/// the Sessions tab — a row means the CLI is hidden; absent = visible.
/// </summary>
public sealed class SessionCliVisibility : Entity<string>
{
    private SessionCliVisibility(string id)
        : base(id)
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public string Cli { get; private set; } = string.Empty;
    public bool Hidden { get; private set; }

    public static string KeyOf(string userId, string cli) => $"{userId}|{cli}";

    public static SessionCliVisibility Create(string userId, string cli, bool hidden) =>
        new(KeyOf(userId, cli))
        {
            UserId = userId,
            Cli = cli,
            Hidden = hidden,
        };

    public void SetHidden(bool hidden) => Hidden = hidden;
}
