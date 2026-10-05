// Shared chat error markers — surfaced to HTTP status codes by the endpoints
// (400 validation / 409 conflict). Lives in Contracts so Integrations tools
// can both throw and catch them (Integrations references Contracts only;
// the namespace keeps the historical Application name for compatibility).

namespace Taskboard.Application.Chat;

/// <summary>Request-level validation failure surfaced as 400.</summary>
public sealed class ChatValidationException(string message) : Exception(message);

/// <summary>Send attempted on an archived conversation — surfaced as 409.</summary>
public sealed class ChatArchivedException(string message) : Exception(message);

/// <summary>Decide attempted on an approval that already left pending — surfaced as 409 (RF-003).</summary>
public sealed class ChatApprovalConflictException(string message) : Exception(message);

/// <summary>
/// SPEC-20261005-chat-fork-steering: steer cap exceeded or cancel attempted on
/// an already-claimed steer — surfaced as 409.
/// </summary>
public sealed class ChatSteerConflictException(string message) : Exception(message);

/// <summary>
/// SPEC-20261005-chat-attachments-feedback: attachment/feedback/schedule-cap
/// conflict — bound-attachment delete, CAS version mismatch, per-conversation
/// limits — surfaced as 409.
/// </summary>
public sealed class ChatConflictException(string message) : Exception(message);

/// <summary>
/// SPEC-20261005-chat-jobs-schedule-search: kill attempted on a job already
/// terminal — surfaced as 409.
/// </summary>
public sealed class ChatJobConflictException(string message) : Exception(message);
