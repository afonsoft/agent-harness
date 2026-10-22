namespace Taskboard.Agents;

/// <summary>
/// Declarative description of one SQLite database (or glob family) a managed
/// agent CLI keeps under <c>$HOME</c>. Paths are stored relative to the home
/// directory — expansion and glob resolution belong to the locator.
/// SPEC-20260919-cli-db-reader RF-001.
/// </summary>
public sealed record CliDbSource(
    string Name,
    string RelativePathPattern,
    IReadOnlyList<string> WhitelistTables,
    IReadOnlyList<string> DeniedTables,
    bool Experimental = false,
    // SPEC-20261020-clidb-large-db-resilience RF-001: per-source cap
    // overrides; null = the reader's global CliDbReadOptions defaults.
    long? MaxCopyFileSizeBytes = null,
    long? MaxInPlaceFileSizeBytes = null)
{
    /// <summary>Whether <see cref="RelativePathPattern"/> contains glob wildcards.</summary>
    public bool IsGlob => RelativePathPattern.Contains('*') || RelativePathPattern.Contains('?');
}
