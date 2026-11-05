using System.Text.Json;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-003: steer a delegated run's
/// persistent terminal — types <paramref name="text"/> + Enter into the PTY.
/// Refuses while the agent is blocked/waiting (unless <c>force</c>) and while
/// a human viewer drives (status_mismatch).
/// </summary>
public sealed class RunSendTool(IRunTerminalService runs) : IChatTool
{
    public string Name => "run_send";
    public string Description =>
        "Send text + Enter to a run terminal (delegated agent PTY). Target is "
        + "a run id, task id, or dispatch id. Refuses while the agent is "
        + "blocked unless force=true, and while a human is driving.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "target":{"type":"string","description":"Run id, task id, or dispatch id"},
          "text":{"type":"string","description":"Text to type (Enter is appended)"},
          "wait":{"type":"boolean","description":"Wait until the turn visibly advances"},
          "force":{"type":"boolean","description":"Bypass the blocked-status refusal"}
        },"required":["target","text"]}
        """;

    public string CapabilityId => "run:send";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var target = RunTerminalToolSupport.ReadString(arguments, "target");
        var text = RunTerminalToolSupport.ReadString(arguments, "text");
        if (target is null || text is null)
        {
            return DelegationToolSupport.Error("target and text are required", "missing arguments");
        }

        var result = await runs.SendAsync(
            target, text,
            wait: RunTerminalToolSupport.ReadBool(arguments, "wait"),
            force: RunTerminalToolSupport.ReadBool(arguments, "force"),
            scope: DelegationToolSupport.ScopeOf(context),
            ct: cancellationToken).ConfigureAwait(false);
        return RunTerminalToolSupport.Result(new
        {
            result.Delivered,
            result.Advanced,
            terminal = RunTerminalToolSupport.Terminal(result.Terminal),
            error = RunTerminalToolSupport.Error(result.Error),
        }, result.Error);
    }
}

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-004: read a run terminal — screen
/// snapshot while busy, scrollback tail when idle, archived output after exit.
/// </summary>
public sealed class RunReadTool(IRunTerminalService runs) : IChatTool
{
    public string Name => "run_read";
    public string Description =>
        "Read a run terminal's output. While the agent is busy you get the "
        + "current screen; when idle (or after exit) the history tail. "
        + "history=true returns the scrollback tail (degraded to screen + "
        + "incomplete flag while the agent works).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "target":{"type":"string","description":"Run id, task id, or dispatch id"},
          "lines":{"type":"integer","description":"Screen lines to return (default 30)"},
          "history":{"type":"boolean","description":"Read the scrollback tail instead of the screen"}
        },"required":["target"]}
        """;

    public string CapabilityId => "run:read";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var target = RunTerminalToolSupport.ReadString(arguments, "target");
        if (target is null)
        {
            return DelegationToolSupport.Error("target is required", "missing target");
        }

        var result = await runs.ReadAsync(
            target,
            lines: RunTerminalToolSupport.ReadInt(arguments, "lines"),
            history: RunTerminalToolSupport.ReadBool(arguments, "history"),
            scope: DelegationToolSupport.ScopeOf(context),
            ct: cancellationToken).ConfigureAwait(false);
        return RunTerminalToolSupport.Result(new
        {
            result.Text,
            result.Incomplete,
            result.Why,
            terminal = RunTerminalToolSupport.Terminal(result.Terminal),
            error = RunTerminalToolSupport.Error(result.Error),
        }, result.Error);
    }
}

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-005: block until the agent reaches a
/// state, post-registration output matches a regex, a mailbox delivery
/// arrives, or the timeout elapses.
/// </summary>
public sealed class RunWaitTool(IRunTerminalService runs) : IChatTool
{
    public string Name => "run_wait";
    public string Description =>
        "Wait on a run terminal: state=<attention state> and/or output=<regex> "
        + "(matches only output emitted after this call). Returns when one "
        + "fires, a mailbox message arrives, or timeout_ms elapses (default "
        + "60000, max 600000).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "target":{"type":"string","description":"Run id, task id, or dispatch id"},
          "state":{"type":"string","description":"Attention state: idle|working|waiting_you|done|blocked"},
          "output":{"type":"string","description":"Regex matched against post-registration output"},
          "timeout_ms":{"type":"integer","description":"Max wait in ms (default 60000, max 600000)"}
        },"required":["target"]}
        """;

    public string CapabilityId => "run:wait";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var target = RunTerminalToolSupport.ReadString(arguments, "target");
        if (target is null)
        {
            return DelegationToolSupport.Error("target is required", "missing target");
        }

        var timeout = RunTerminalToolSupport.ReadInt(arguments, "timeout_ms") is { } ms
            ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 100, 600_000))
            : (TimeSpan?)null;
        var result = await runs.WaitAsync(
            new RunWaitRequest(
                target,
                RunTerminalToolSupport.ReadString(arguments, "state"),
                RunTerminalToolSupport.ReadString(arguments, "output"),
                timeout),
            scope: DelegationToolSupport.ScopeOf(context),
            ct: cancellationToken).ConfigureAwait(false);
        return RunTerminalToolSupport.Result(new
        {
            reason = result.Reason.ToString().ToLowerInvariant(),
            result.State,
            result.Matched,
            terminal = RunTerminalToolSupport.Terminal(result.Terminal),
            error = RunTerminalToolSupport.Error(result.Error),
        }, result.Error);
    }
}

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-006: interrupt the current turn
/// (Ctrl+C/Esc per CLI) — never kills the process.
/// </summary>
public sealed class RunInterruptTool(IRunTerminalService runs) : IChatTool
{
    public string Name => "run_interrupt";
    public string Description =>
        "Interrupt the agent's current turn in a run terminal (the CLI's "
        + "interrupt sequence — Ctrl+C/Esc). Stops the turn, keeps the "
        + "process alive; the terminal stays steerable.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "target":{"type":"string","description":"Run id, task id, or dispatch id"}
        },"required":["target"]}
        """;

    public string CapabilityId => "run:interrupt";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var target = RunTerminalToolSupport.ReadString(arguments, "target");
        if (target is null)
        {
            return DelegationToolSupport.Error("target is required", "missing target");
        }

        var result = await runs.InterruptAsync(
            target, DelegationToolSupport.ScopeOf(context), cancellationToken).ConfigureAwait(false);
        return RunTerminalToolSupport.Result(new
        {
            result.Delivered,
            terminal = RunTerminalToolSupport.Terminal(result.Terminal),
            error = RunTerminalToolSupport.Error(result.Error),
        }, result.Error);
    }
}

/// <summary>Arg readers + JSON projection shared by the run_* tools.</summary>
public static class RunTerminalToolSupport
{
    public static string? ReadString(JsonElement args, string name) =>
        DelegationToolSupport.ReadString(args, name);

    public static bool ReadBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    public static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : null;

    /// <summary>
    /// Tool-result payload: ok envelope when no structured error; refused
    /// (with the error kind as the refusal reason) otherwise — the model sees
    /// the same JSON either way.
    /// </summary>
    public static ChatToolResult Result(object payload, RunTerminalError? error) =>
        error is null
            ? DelegationToolSupport.Ok(payload)
            : new ChatToolResult(
                JsonSerializer.Serialize(payload), Refused: true, error.Kind);

    public static object? Terminal(RunTerminalDto? dto) =>
        dto is null
            ? null
            : new
            {
                dto.Id,
                dto.TaskId,
                dto.DispatchId,
                dto.CliName,
                state = dto.State.ToString().ToLowerInvariant(),
                owner = dto.ControlOwner.ToString().ToLowerInvariant(),
                dto.Status,
                dto.ExitCode,
                dto.FirstMessageSent,
                dto.FirstMessageReason,
            };

    public static object? Error(RunTerminalError? error) =>
        error is null ? null : new { error.Kind, error.Message, error.Hint };
}
