namespace Taskboard.Dtos;

public sealed record WorkspaceDiffFileDto(string Path, string Status, int Insertions = 0, int Deletions = 0);

public sealed record WorkspaceDiffDto(
    int FilesChanged,
    int Insertions,
    int Deletions,
    IReadOnlyList<WorkspaceDiffFileDto> Files,
    string Patch);
