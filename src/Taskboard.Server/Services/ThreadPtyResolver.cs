using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Agents;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
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
    IAgentDiscoveryService discovery)
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

        var workdir = !string.IsNullOrWhiteSpace(thread.WorkspacePath)
            ? thread.WorkspacePath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Command: custom definition or builtin binary.
        List<string> argv;
        if (!string.IsNullOrWhiteSpace(thread.AgentCliId))
        {
            var def = await cliDefinitions.GetAsync(thread.AgentCliId, ct).ConfigureAwait(false);
            if (def is null)
            {
                return (null, $"Custom CLI '{thread.AgentCliId}' no longer exists.");
            }

            if (!def.Enabled)
            {
                return (null, $"Custom CLI '{def.DisplayName}' is disabled.");
            }

            argv = [def.Executable];
            var model = string.Equals(thread.Model, "default", StringComparison.Ordinal)
                ? null
                : thread.Model;
            argv.AddRange(AgentCliArgsTemplate.Render(def.ArgsTemplate, def.ModelFlag, model: model));
        }
        else if (!string.IsNullOrWhiteSpace(thread.AgentType)
                 && Enum.TryParse<AgentType>(thread.AgentType, ignoreCase: true, out var agentType))
        {
            var kind = AgentCliMap.CliKindFor(agentType);
            var spec = kind is null ? null : AgentCliMap.GetSpec(kind.Value);
            var binary = spec?.Binary ?? (agentType == AgentType.OpenHands ? "openhands" : null);
            if (binary is null)
            {
                return (null, $"No known CLI binary for agent '{thread.AgentType}'.");
            }

            var path = discovery.ResolveExecutablePath(agentType);
            argv = [path ?? binary];
        }
        else
        {
            return (null, "Thread has no CLI binding.");
        }

        // Docker context: wrap argv in `docker exec -it <container>`.
        if (!string.IsNullOrWhiteSpace(thread.ContainerContext))
        {
            if (!DockerCliSpawner.IsValidContainerName(thread.ContainerContext))
            {
                return (null, $"Invalid container name '{thread.ContainerContext}'.");
            }

            argv = ["docker", .. DockerCliSpawner.BuildExecArgs(thread.ContainerContext, argv, interactive: true)];
        }

        return (new Resolution(workdir, argv), null);
    }
}
