namespace Taskboard.Agents;

/// <summary>
/// Thrown when an external CLI database path escapes <c>$HOME</c> or a query
/// violates the whitelist/denylist declared by a <see cref="CliDbSource"/>
/// (SPEC-20260919-cli-db-reader RF-002/RF-004 — `CLIDB_ACCESS_DENIED`).
/// </summary>
public sealed class CliDbAccessDeniedException : DomainException
{
    public CliDbAccessDeniedException(string message)
        : base(TaskboardDomainErrorCodes.CliDbAccessDenied, message)
    {
    }
}

/// <summary>
/// Thrown when an external database cannot be read (missing, oversized,
/// locked, corrupt). Callers map this to <see cref="CliDbSourceStatus.Error"/>
/// — it never propagates raw vendor errors upstream.
/// </summary>
public sealed class CliDbReadException : DomainException
{
    public CliDbReadException(string message)
        : base(TaskboardDomainErrorCodes.CliDbReadFailed, message)
    {
    }
}
