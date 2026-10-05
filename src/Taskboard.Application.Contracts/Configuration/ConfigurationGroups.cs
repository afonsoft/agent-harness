namespace Taskboard.Application.Contracts.Configuration;

/// <summary>
/// Display categories for the Settings Configuration tab
/// (SPEC-20261010-settings-configuration-tab). The order of the constants is
/// the order the sections render.
/// </summary>
public static class ConfigurationGroups
{
    public const string Connections = "Connections";
    public const string Server = "Server";
    public const string Logging = "Logging";
    public const string Chat = "Chat";
    public const string Security = "Security";

    /// <summary>Groups in render order.</summary>
    public static readonly IReadOnlyList<string> Ordered =
        [Connections, Server, Logging, Chat, Security];
}
