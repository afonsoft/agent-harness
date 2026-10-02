using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Agents;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20260928-ai-code-generic-cli RF-003/RF-004: resolves the argv a
/// PTY-backed AI Code thread must spawn — builtin CLI binary or a custom
/// <see cref="AgentCliDefinitionDto"/> rendered through its args template —
/// wrapped in <c>docker exec -it</c> when the thread carries a container
/// context. Never shell-interpolated: argv only.
/// </summary>
public sealed class ThreadPtyResolver(
    AiChatService aiChatService,
    IAgentCliDefinitionRepository cliDefinitions,
    IAgentDiscoveryService discovery,
    DockerCliDiscovery dockerDiscovery,
    WorkspaceService workspace)
{
    /// <summary>Deterministic TerminalHub session id for a thread (reattach key).</summary>
    public static string SessionIdFor(string threadId) => $"t-{threadId}";

    public sealed record Resolution(string WorkingDirectory, IReadOnlyList<string> Command);

    /// <summary>
    /// Resolves (workdir, argv) or returns null with an error message.
    /// </summary>
    public async Task<(Resolution? Result, string? Error)> ResolveAsync(
        string threadId, CancellationToken ct = default)
    {
        var thread = await aiChatService.GetThreadAsync(AiChatThreadId.From(threadId), ct)
            .ConfigureAwait(false);
        if (thread is null)
        {
            return (null, "Thread not found.");
        }

        if (!string.Equals(thread.Transport, "pty", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Thread is not a terminal (pty) thread.");
        }

        var workdir = ResolveWorkdir(thread);

        var (argv, argvError) = await ResolveArgvAsync(thread, ct).ConfigureAwait(false);
        if (argv is null)
        {
            return (null, argvError);
        }

        var (wrapped, wrapError) = await WrapForContainerAsync(thread, argv, ct).ConfigureAwait(false);
        if (wrapped is null)
        {
            return (null, wrapError);
        }

        return (new Resolution(workdir, wrapped), null);
    }

    // Same workdir rule as AgentSessionManager.EnsureSessionAsync
    // (SPEC-20260929-pty-session-security RF-006): explicit workspace →
    // repo card dir → workspace root — never the bare user profile.
    private string ResolveWorkdir(AiChatThreadDto thread)
    {
        if (!string.IsNullOrWhiteSpace(thread.WorkspacePath))
        {
            return thread.WorkspacePath;
        }

        return !string.IsNullOrWhiteSpace(thread.RepositoryFullName)
            ? workspace.ResolveCardWorkdir(thread.RepositoryFullName, out _)
            : workspace.EnsureRoot();
    }

    // Command: custom definition or builtin binary.
    private async Task<(List<string>? Argv, string? Error)> ResolveArgvAsync(
        AiChatThreadDto thread, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(thread.AgentCliId))
        {
            return await ResolveCustomArgvAsync(thread, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentType)
            && Enum.TryParse<AgentType>(thread.AgentType, ignoreCase: true, out var agentType))
        {
            return ResolveBuiltinArgv(thread, agentType);
        }

        return (null, "Thread has no CLI binding.");
    }

    private async Task<(List<string>? Argv, string? Error)> ResolveCustomArgvAsync(
        AiChatThreadDto thread, CancellationToken ct)
    {
        var def = await cliDefinitions.GetAsync(thread.AgentCliId!, ct).ConfigureAwait(false);
        if (def is null)
        {
            return (null, $"Custom CLI '{thread.AgentCliId}' no longer exists.");
        }

        if (!def.Enabled)
        {
            return (null, $"Custom CLI '{def.DisplayName}' is disabled.");
        }

        var argv = new List<string> { def.Executable };
        var model = string.Equals(thread.Model, "default", StringComparison.Ordinal)
            ? null
            : thread.Model;
        argv.AddRange(AgentCliArgsTemplate.Render(def.ArgsTemplate, def.ModelFlag, model: model));
        return (argv, null);
    }

    private (List<string>? Argv, string? Error) ResolveBuiltinArgv(
        AiChatThreadDto thread, AgentType agentType)
    {
        var kind = AgentCliMap.CliKindFor(agentType);
        var spec = kind is null ? null : AgentCliMap.GetSpec(kind.Value);
        var binary = spec?.Binary ?? (agentType == AgentType.OpenHands ? "openhands" : null);
        if (binary is null)
        {
            return (null, $"No known CLI binary for agent '{thread.AgentType}'.");
        }

        // Inside a container the binary is resolved by name — a host path
        // would not exist there (SPEC-20260929-docker-cli-context RF-001).
        var inContainer = !string.IsNullOrWhiteSpace(thread.ContainerContext);
        var path = inContainer ? null : discovery.ResolveExecutablePath(agentType);
        return ([path ?? binary], null);
    }

    // Docker context: wrap argv in `docker exec -it <container>` — but
    // only for containers the discovery actually sees running. A persisted
    // name passing only syntax validation would let a caller exec into any
    // container the daemon can reach (SPEC-20260929-pty-session-security
    // RF-002).
    private async Task<(List<string>? Argv, string? Error)> WrapForContainerAsync(
        AiChatThreadDto thread, List<string> argv, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(thread.ContainerContext))
        {
            return (argv, null);
        }

        if (!DockerCliSpawner.IsValidContainerName(thread.ContainerContext))
        {
            return (null, $"Invalid container name '{thread.ContainerContext}'.");
        }

        var containers = await dockerDiscovery.ListContainersAsync(ct).ConfigureAwait(false);
        if (containers.All(c => !string.Equals(c.Name, thread.ContainerContext, StringComparison.Ordinal)))
        {
            return (null, $"Container '{thread.ContainerContext}' is not running or not allowed.");
        }

        List<string> wrapped =
            ["docker", .. DockerCliSpawner.BuildExecArgs(thread.ContainerContext, argv, interactive: true)];
        return (wrapped, null);
    }
}
