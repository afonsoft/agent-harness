using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Cross-session memory record — facts, architectural decisions and lessons
/// learned scoped to a repository (SPEC-20260919-harness-context-memory RF-004).
/// Metadata only — never holds secrets or credentials.
/// </summary>
public sealed class ProjectMemoryItem : AggregateRoot<ProjectMemoryItemId>
{
    public string RepositoryFullName { get; private set; } = default!;
    public string Topic { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public MemoryType Type { get; private set; }
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ProjectMemoryItem()
    {
    }

    private ProjectMemoryItem(
        ProjectMemoryItemId id,
        string repositoryFullName,
        string topic,
        string content,
        MemoryType type,
        IReadOnlyList<string> tags,
        DateTime now)
        : base(id)
    {
        ValidateRepositoryFullName(repositoryFullName);
        ValidateContent(content);
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Topic cannot be empty.");
        }

        RepositoryFullName = repositoryFullName;
        Topic = topic.Trim();
        Content = content.Trim();
        Type = type;
        Tags = NormalizeTags(tags);
        CreatedAt = UpdatedAt = now;
    }

    public static ProjectMemoryItem Create(
        ProjectMemoryItemId id,
        string repositoryFullName,
        string topic,
        string content,
        MemoryType type = MemoryType.Fact,
        IReadOnlyList<string>? tags = null,
        DateTime? now = null)
        => new(id, repositoryFullName, topic, content, type, tags ?? [], now ?? DateTime.UtcNow);

    public void UpdateContent(string content, IReadOnlyList<string>? tags = null, DateTime? now = null)
    {
        ValidateContent(content);
        Content = content.Trim();
        if (tags is not null)
        {
            Tags = NormalizeTags(tags);
        }

        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    private static void ValidateRepositoryFullName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('/'))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "RepositoryFullName must be in 'owner/name' form.");
        }
    }

    private static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Content cannot be empty.");
        }
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tags)
        => tags
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
}
