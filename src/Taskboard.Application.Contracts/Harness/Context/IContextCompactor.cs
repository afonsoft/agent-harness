using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Multi-turn history compaction: snip → collapse → summarize when token
/// usage exceeds 80% of the model window (SPEC RF-003). Preserves the system
/// prompt and the latest user instruction intact.
/// </summary>
public interface IContextCompactor
{
    Task<CompactionResultDto> CompactIfNeededAsync(
        IReadOnlyList<ContextMessageDto> messages,
        int maxTokens,
        CancellationToken cancellationToken = default);
}
