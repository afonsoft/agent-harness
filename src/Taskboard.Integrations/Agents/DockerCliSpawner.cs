using System.Diagnostics.CodeAnalysis;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20260928-ai-code-generic-cli RF-004: builds <c>docker exec</c> argv so
/// a CLI runs inside an already-running container — <c>-it</c> for PTY
/// sessions (the outer PTY is ours), <c>-i</c> for stdin/stdout transports.
/// Args are passed as an array — never shell-interpolated.
/// </summary>
public static class DockerCliSpawner
{
    /// <summary>
    /// Builds the argv for <c>docker exec [-it|-i] &lt;container&gt; &lt;cmd…&gt;</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildExecArgs(string container, IReadOnlyList<string> command, bool interactive)
    {
        var args = new List<string> { "exec", interactive ? "-it" : "-i", container };
        args.AddRange(command);
        return args;
    }

    /// <summary>Validates a container name — [A-Za-z0-9][A-Za-z0-9_.-]+ (docker name rules).</summary>
    public static bool IsValidContainerName([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 128
        && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9_.\-/]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
}
