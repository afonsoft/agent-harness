namespace Taskboard;

public static class TaskboardDomainErrorCodes
{
    public const string InvalidValue = "Taskboard:00001";
    public const string InvalidActorType = "Taskboard:00008";
    public const string VersionConflict = "Taskboard:00015";
    public const string EmptyTitle = "Taskboard:00020";
    public const string SecurityAccessDenied = "Taskboard:00025";
}
