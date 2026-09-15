namespace Taskboard.Blazor.Services;

/// <summary>
/// Holds the authenticated user's cookie for the current Blazor Server circuit
/// so that <see cref="TaskboardClient"/> self-calls to the API can forward it.
/// </summary>
public sealed class CircuitAuthContext
{
    /// <summary>Raw <c>Cookie</c> header value captured during prerender; null when unauthenticated.</summary>
    public string? Cookie { get; set; }
}
