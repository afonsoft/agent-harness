using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Terminal;

/// <summary>Creates <see cref="PtySession"/> instances bound to the effective home directory.</summary>
public sealed class PtySessionFactory(string homeDirectory, ILoggerFactory loggerFactory)
{
    /// <param name="workingDirectory">Optional cwd override (SPEC-20260920
    /// RF-006 — selected repo workdir resolved server-side); null → home.</param>
    public PtySession Create(string? workingDirectory = null, int cols = 120, int rows = 30) =>
        new(homeDirectory, loggerFactory.CreateLogger<PtySession>(), cols, rows,
            workingDirectory: workingDirectory);
}
