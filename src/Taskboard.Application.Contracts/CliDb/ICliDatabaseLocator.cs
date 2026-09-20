using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliDb;

/// <summary>
/// Resolves <see cref="CliDbSource"/> patterns to existing absolute paths under
/// <c>$HOME</c> and reports per-source status. Never creates files or dirs.
/// SPEC-20260919-cli-db-reader RF-006.
/// </summary>
public interface ICliDatabaseLocator
{
    /// <summary>Existing absolute paths matching <paramref name="source"/> (empty when missing).</summary>
    IReadOnlyList<string> Resolve(CliDbSource source);

    /// <summary>File signature for change detection, or null when absent.</summary>
    CliDbFileStat? Stat(string absolutePath);

    /// <summary>Per-kind, per-file status across the whole registry.</summary>
    IReadOnlyList<CliDbSourceStatusDto> GetStatus();
}

/// <summary>Last-write time + size of an external database file.</summary>
public sealed record CliDbFileStat(DateTime ModifiedUtc, long SizeBytes);
