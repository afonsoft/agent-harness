using System.Collections.Concurrent;
using Taskboard.Dtos;

namespace Taskboard.Application.AiChat;

/// <summary>
/// In-memory store for user-added AI chat model entries
/// (SPEC-20260921-ai-chat-cli-backend RF-007). The real catalog is built by
/// <see cref="AiChatCatalogService"/> from the eligible agent CLIs — no
/// hardcoded provider models live here anymore.
/// </summary>
public sealed class AiCatalogService
{
    private readonly ConcurrentDictionary<string, AiChatModelDto> _models = new();

    public IReadOnlyCollection<AiChatModelDto> List() => _models.Values.ToList().AsReadOnly();

    public bool TryAdd(AiChatModelDto model) => _models.TryAdd(model.Id, model);

    public bool Contains(string modelId) => _models.ContainsKey(modelId);
}
