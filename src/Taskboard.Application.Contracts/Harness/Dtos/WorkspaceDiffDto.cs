namespace Taskboard.Dtos;

public sealed record WorkspaceDiffFileDto(string Path, string Status);

public sealed record WorkspaceDiffDto(
    int FilesChanged,
    int Insertions,
    int Deletions,
    IReadOnlyList<WorkspaceDiffFileDto> Files,
    string Patch);
