using System.Text;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Adaptador para CLIs de agentes conhecidos que recebem o prompt como argumento.
/// </summary>
public sealed class KnownCliAgentAdapter : IAgentAdapter
{
    private readonly WorkspaceService? _workspace;

    public KnownCliAgentAdapter(WorkspaceService? workspace = null)
    {
        _workspace = workspace;
    }

    public bool CanHandle(AgentType agentType) => AgentCliInvocation.ExecutableName(agentType) is not null;

    public bool SupportsInteractiveSession => true;

    public AgentCommand BuildCommand(AgentExecutionRequest request)
    {
        var name = AgentCliInvocation.ExecutableName(request.AgentType)
                   ?? throw new NotSupportedException($"Agent type {request.AgentType} is not supported.");

        var executablePath = PathSearch.FindExecutable(name)
                             ?? throw new FileNotFoundException($"Executable '{name}' not found in PATH.");

        var prompt = BuildPrompt(request);
        var workingDirectory = !string.IsNullOrWhiteSpace(request.RepoPath)
            ? request.RepoPath
            : _workspace?.EnsureRoot() ?? Environment.CurrentDirectory;

        return new AgentCommand(
            executablePath,
            AgentCliInvocation.BuildArguments(
                request.AgentType, prompt, request.ModelTier, request.ResolvedModelName, request.OmitModelFlag),
            workingDirectory);
    }

    public AgentCommand BuildSessionCommand(AgentType agentType, string workdir, Sandbox sandbox, string? modelName = null)
    {
        var name = AgentCliInvocation.ExecutableName(agentType)
                   ?? throw new NotSupportedException($"Agent type {agentType} is not supported.");

        var executablePath = PathSearch.FindExecutable(name) ?? name;
        var arguments = BuildSessionArguments(agentType, sandbox, modelName);
        var workingDirectory = !string.IsNullOrWhiteSpace(workdir)
            ? workdir
            : _workspace?.EnsureRoot() ?? Environment.CurrentDirectory;

        return new AgentCommand(executablePath, arguments, workingDirectory);
    }

    private static IReadOnlyList<string> BuildSessionArguments(AgentType agentType, Sandbox sandbox, string? modelName)
    {
        // Explicit model only — sessions never inject the curated Normal model.
        var flag = AgentCliModels.ModelFlag(agentType);
        var model = flag is not null && !string.IsNullOrWhiteSpace(modelName) ? new[] { flag, modelName } : [];

        return agentType switch
        {
            AgentType.OpenCode => ["acp", .. model],
            AgentType.Claude => ["--acp", .. model],
            AgentType.Codex => ["--acp", .. model],
            _ => ["--acp", .. model]
        };
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
