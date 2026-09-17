using System.Text;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Adaptador para CLIs de agentes conhecidos que recebem o prompt como argumento.
/// </summary>
public sealed class KnownCliAgentAdapter : IAgentAdapter
{
    private static readonly Dictionary<AgentType, string> ExecutableNames = new()
    {
        [AgentType.Devin] = "devin",
        [AgentType.Claude] = "claude",
        [AgentType.Codex] = "codex",
        [AgentType.OpenCode] = "opencode",
        [AgentType.OpenHands] = "openhands",
        [AgentType.Antigravity] = "agy"
    };

    public bool CanHandle(AgentType agentType) => ExecutableNames.ContainsKey(agentType);

    public AgentCommand BuildCommand(AgentExecutionRequest request)
    {
        if (!ExecutableNames.TryGetValue(request.AgentType, out var name))
        {
            throw new NotSupportedException($"Agent type {request.AgentType} is not supported.");
        }

        var executablePath = PathSearch.FindExecutable(name)
                             ?? throw new FileNotFoundException($"Executable '{name}' not found in PATH.");

        var prompt = BuildPrompt(request);
        var workingDirectory = string.IsNullOrWhiteSpace(request.RepoPath)
            ? Environment.CurrentDirectory
            : request.RepoPath;

        return new AgentCommand(executablePath, [prompt], workingDirectory);
    }

    private static string BuildPrompt(AgentExecutionRequest request)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            builder.AppendLine($"Branch: {request.Branch}");
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            builder.AppendLine($"Scope: {request.Scope}");
        }

        builder.AppendLine(request.Instructions);

        return builder.ToString().Trim();
    }
}
