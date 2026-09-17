using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Terminal;

/// <summary>Creates <see cref="PtySession"/> instances bound to the effective home directory.</summary>
public sealed class PtySessionFactory(string homeDirectory, ILoggerFactory loggerFactory)
{
    public PtySession Create(int cols = 120, int rows = 30) =>
        new(homeDirectory, loggerFactory.CreateLogger<PtySession>(), cols, rows);
}
