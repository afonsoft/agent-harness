namespace Taskboard.Dtos;

/// <summary>Uma entrada de diretório do worktree (SPEC-20260921-cockpit-live-logs-explorer-diff RF-003).</summary>
public sealed record WorktreeEntryDto(string Name, string Path, bool Directory, long? SizeBytes);

/// <summary>Listagem de um diretório do worktree — <see cref="Truncated"/> quando o cap de entradas é atingido.</summary>
public sealed record WorktreeListDto(string Path, IReadOnlyList<WorktreeEntryDto> Entries, bool Truncated);

/// <summary>Conteúdo de um arquivo do worktree; <see cref="Content"/> é nulo quando <see cref="Binary"/>.</summary>
public sealed record WorktreeFileContentDto(string Path, string? Content, long SizeBytes, bool Truncated, bool Binary);
