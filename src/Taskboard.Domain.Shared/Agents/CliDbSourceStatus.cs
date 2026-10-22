namespace Taskboard.Agents;

/// <summary>
/// Resolution/health state of a <see cref="CliDbSource"/>.
/// SPEC-20260919-cli-db-reader RF-006.
/// </summary>
public enum CliDbSourceStatus
{
    /// <summary>File exists and is readable in place.</summary>
    Available,

    /// <summary>Pattern resolved to nothing — CLI not installed or DB absent.</summary>
    Missing,

    /// <summary>Source is WAL/busy; reads happen against a deleted-after-use temp copy.</summary>
    CopiedToTemp,

    /// <summary>Schema fingerprint differs from the extractor's expected fingerprint.</summary>
    SchemaDrifted,

    /// <summary>
    /// SPEC-20261020-clidb-large-db-resilience RF-004: read via the
    /// <c>immutable=1</c> fallback — a static committed snapshot that may lag
    /// the live DB by the WAL tail. Distinct from <see cref="CopiedToTemp"/>:
    /// no copy was made.
    /// </summary>
    ReadStale,

    /// <summary>Unreadable (permissions, corrupt header, size cap, timeout).</summary>
    Error
}
