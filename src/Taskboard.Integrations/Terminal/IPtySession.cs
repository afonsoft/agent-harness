namespace Taskboard.Integrations.Terminal;

/// <summary>
/// Abstraction over an interactive PTY-backed shell session — lets the
/// session registry be unit-tested with fakes (SPEC-20260917-terminal-tabs).
/// </summary>
public interface IPtySession : IAsyncDisposable
{
    /// <summary>Raised for each chunk of terminal output (UTF-8 text, may contain ANSI escapes).</summary>
    event Action<string>? OutputReceived;

    /// <summary>Raised once when the shell process exits.</summary>
    event Action<int>? Exited;

    /// <summary>Last time input was written — drives the idle timeout.</summary>
    DateTimeOffset LastActivityUtc { get; }

    /// <summary>Whether the underlying process is still running.</summary>
    bool IsRunning { get; }

    /// <summary>Spawns the underlying shell process.</summary>
    void Start();

    /// <summary>Writes raw input to the shell (keys, paste, control chars).</summary>
    Task WriteAsync(string data);

    /// <summary>Resizes the PTY.</summary>
    Task ResizeAsync(int cols, int rows);
}
