namespace Taskboard;

public static class TaskboardDomainErrorCodes
{
    public const string InvalidValue = "Taskboard:00001";
    public const string InvalidActorType = "Taskboard:00008";
    public const string VersionConflict = "Taskboard:00015";
    public const string EmptyTitle = "Taskboard:00020";
    public const string SecurityAccessDenied = "Taskboard:00025";
    public const string CliDbAccessDenied = "Taskboard:00030";
    public const string CliDbReadFailed = "Taskboard:00031";
    public const string InvalidPipelineDag = "Taskboard:00032";
    public const string InvalidSpecStatus = "Taskboard:00033";
}
