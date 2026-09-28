namespace Taskboard.Agents;

/// <summary>
/// How a thread talks to the backing CLI (SPEC-20260928-ai-code-generic-cli):
/// <see cref="Acp"/> = JSON-RPC structured session; <see cref="Pty"/> = raw
/// interactive terminal passthrough (xterm.js, zero parsing).
/// </summary>
public enum CliTransport
{
    Acp,
    Pty,
}
