using Taskboard;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// An OpenAI-compatible chat provider registered by the operator
/// (SPEC-20260929-ai-code-provider-chat RF-001). The API key never leaves the
/// server: DTOs expose only a masked hint.
/// </summary>
public sealed class ChatProvider : Entity<Guid>
{
    public string Name { get; private set; } = default!;
    public string BaseUrl { get; private set; } = default!;
    public string ApiKey { get; private set; } = default!;
    public bool Enabled { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ChatProvider()
    {
    }

    private ChatProvider(Guid id, string name, string baseUrl, string apiKey, bool enabled, DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Provider name cannot be empty.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Provider base URL must be an absolute http(s) URL.");
        }

        Name = name.Trim();
        BaseUrl = baseUrl.TrimEnd('/');
        ApiKey = apiKey;
        Enabled = enabled;
        CreatedAt = UpdatedAt = now;
    }

    public static ChatProvider Create(
        string name, string baseUrl, string apiKey, DateTime? now = null, bool enabled = true) =>
        new(Guid.NewGuid(), name, baseUrl, apiKey, enabled, now ?? DateTime.UtcNow);

    public void Update(string? name, string? baseUrl, string? apiKey, bool? enabled, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Provider base URL must be an absolute http(s) URL.");
            }

            BaseUrl = baseUrl.TrimEnd('/');
        }

        // Empty/absent key keeps the stored one — the UI sends the key only when edited.
        if (!string.IsNullOrEmpty(apiKey))
        {
            ApiKey = apiKey;
        }

        if (enabled.HasValue)
        {
            Enabled = enabled.Value;
        }

        UpdatedAt = now;
    }
}
