using Microsoft.Extensions.Configuration;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Per-feature kill switches (SPEC-20261005-chat-jobs-schedule-search RNF-004)
/// — all default-on; the endpoints and the tools check the same keys so
/// nothing mutates while a feature is off.
/// </summary>
public static class ChatFeatureFlags
{
    public const string JobsEnabledKey = "Taskboard:Chat:Jobs:Enabled";
    public const string ScheduleEnabledKey = "Taskboard:Chat:Schedule:Enabled";
    public const string SearchEnabledKey = "Taskboard:Chat:Search:Enabled";

    public static bool IsEnabled(IConfiguration configuration, string key) =>
        !string.Equals(configuration[key], "false", StringComparison.OrdinalIgnoreCase);
}
