using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Terminal;

/// <summary>Creates <see cref="PtySession"/> instances bound to the effective home directory.</summary>
public sealed class PtySessionFactory(string homeDirectory, ILoggerFactory loggerFactory)
{
    /// <summary>Spawns a new PTY session bound to the configured home directory.</summary>
    /// <param name="workingDirectory">Optional cwd override (SPEC-20260920
    /// RF-006 — selected repo workdir resolved server-side); null → home.</param>
    /// <param name="command">Arbitrary argv for the PTY (SPEC-20260928 —
    /// agent CLIs, docker exec); null → login bash.</param>
    public PtySession Create(string? workingDirectory = null, int cols = 120, int rows = 30,
        IReadOnlyList<string>? command = null) =>
        new(homeDirectory, loggerFactory.CreateLogger<PtySession>(), cols, rows,
            workingDirectory: workingDirectory, command: command);
}
