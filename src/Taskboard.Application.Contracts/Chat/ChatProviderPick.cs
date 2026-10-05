namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Default provider pick for the chat composer (SPEC-20261004-provider-pick-hybridcache
/// RF-001): the catalog is alphabetical, so "first" is not necessarily usable.
/// Prefer the first enabled provider with an API key, then any enabled
/// provider, then fall back to the first entry so a fully-unavailable list
/// still behaves as before (the disabled-provider error surfaces on send).
/// </summary>
public static class ChatProviderPick
{
    public static ChatProviderDto? PreferAvailable(IReadOnlyList<ChatProviderDto> providers)
    {
        if (providers.Count == 0)
        {
            return null;
        }

        return providers.FirstOrDefault(p => p.Enabled && p.HasApiKey)
            ?? providers.FirstOrDefault(p => p.Enabled)
            ?? providers[0];
    }
}
