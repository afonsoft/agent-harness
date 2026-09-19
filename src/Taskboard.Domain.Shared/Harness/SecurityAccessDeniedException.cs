namespace Taskboard.Harness;

/// <summary>
/// Thrown when a path or command escapes the sandbox boundary
/// (SPEC-20260919-harness-security-permission-gateway RF-002 — `SECURITY_ACCESS_DENIED`).
/// </summary>
public sealed class SecurityAccessDeniedException : DomainException
{
    public SecurityAccessDeniedException(string message)
        : base(TaskboardDomainErrorCodes.SecurityAccessDenied, message)
    {
    }
}
