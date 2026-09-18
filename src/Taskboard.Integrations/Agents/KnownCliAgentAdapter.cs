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

        return new AgentCommand(executablePath, BuildArguments(request.AgentType, prompt), workingDirectory);
    }

    private static IReadOnlyList<string> BuildArguments(AgentType agentType, string prompt) => agentType switch
    {
        // devin [PATH]... exige -p/--print para modo não-interativo; sem ele o prompt vira PATH.
        // --respect-workspace-trust false: print mode falha em diretório não confiável.
        AgentType.Devin => ["--respect-workspace-trust", "false", "-p", prompt],
        // claude -p para modo não-interativo; sem TTY as permissões precisam ser ignoradas.
        AgentType.Claude => ["--dangerously-skip-permissions", "-p", prompt],
        // codex exec é o modo não-interativo; --approve-for-me auto-aprova via sandbox workspace-write.
        AgentType.Codex => ["exec", "--approve-for-me", "--skip-git-repo-check", prompt],
        // opencode run executa uma mensagem e sai.
        AgentType.OpenCode => ["run", prompt],
        // agy -p/--print executa um prompt único e sai.
        AgentType.Antigravity => ["-p", prompt],
        _ => [prompt]
    };

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
