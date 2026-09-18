using System.Runtime.InteropServices;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

[Collection("PathEnvironment")]
public class KnownCliAgentAdapterTests
{
    [Theory]
    [InlineData(AgentType.Devin)]
    [InlineData(AgentType.Claude)]
    [InlineData(AgentType.Codex)]
    [InlineData(AgentType.OpenCode)]
    [InlineData(AgentType.OpenHands)]
    public void Dado_TipoDeAgenteConhecido_Quando_VerificarSuporte_Entao_RetornaVerdadeiro(AgentType agentType)
    {
        var adapter = new KnownCliAgentAdapter();

        adapter.CanHandle(agentType).ShouldBeTrue();
    }

    [Fact]
    public void Dado_CodexDisponivelNoPath_Quando_MontarComando_Entao_UsaExecComPromptComoUltimoArgumento()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var executablePath = Path.Combine(directory.FullName, "codex");
        File.WriteAllText(executablePath, "#!/bin/sh\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);
            var request = CriarRequest(AgentType.Codex);

            var command = new KnownCliAgentAdapter().BuildCommand(request);

            command.ExecutablePath.ShouldBe(executablePath);
            command.WorkingDirectory.ShouldBe(request.RepoPath);
            command.Arguments.ShouldBe(["exec", "--approve-for-me", "--skip-git-repo-check", "-m", "gpt-5.6-luna", command.Arguments[5]]);
            command.Arguments[5].ShouldContain($"Branch: {request.Branch}");
            command.Arguments[5].ShouldContain($"Scope: {request.Scope}");
            command.Arguments[5].ShouldContain(request.Instructions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dado_DevinDisponivelNoPath_Quando_MontarComando_Entao_PromptVaiAposPrintSemVirarPath()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var executablePath = Path.Combine(directory.FullName, "devin");
        File.WriteAllText(executablePath, "#!/bin/sh\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);
            var request = CriarRequest(AgentType.Devin) with
            {
                Instructions = "Bumps Npgsql.\n\n<details>\n<summary>Release notes</summary>\n</details>"
            };

            var command = new KnownCliAgentAdapter().BuildCommand(request);

            command.Arguments.Count.ShouldBe(6);
            command.Arguments[0].ShouldBe("--respect-workspace-trust");
            command.Arguments[1].ShouldBe("false");
            command.Arguments[2].ShouldBe("--model");
            command.Arguments[3].ShouldBe("swe");
            command.Arguments[4].ShouldBe("-p");
            command.Arguments[5].ShouldContain("</details>");
            command.Arguments[5].ShouldContain(request.Instructions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData(AgentType.Claude, new[] { "--dangerously-skip-permissions", "--model", "sonnet", "-p" })]
    [InlineData(AgentType.OpenCode, new[] { "run", "--auto", "-m", "opencode/claude-sonnet-5" })]
    [InlineData(AgentType.Antigravity, new[] { "--dangerously-skip-permissions", "--model", "gemini-3.1-pro-low", "-p" })]
    public void Dado_CliConhecido_Quando_MontarComando_Entao_PromptEhUltimoArgumento(AgentType agentType, string[] prefix)
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var executablePath = Path.Combine(
            directory.FullName,
            agentType switch
            {
                AgentType.Claude => "claude",
                AgentType.OpenCode => "opencode",
                _ => "agy"
            });
        File.WriteAllText(executablePath, "#!/bin/sh\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);
            var request = CriarRequest(agentType);

            var command = new KnownCliAgentAdapter().BuildCommand(request);

            command.Arguments.Count.ShouldBe(prefix.Length + 1);
            command.Arguments.Take(prefix.Length).ShouldBe(prefix);
            command.Arguments[^1].ShouldContain(request.Instructions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dado_RepoPathVazio_Quando_MontarComando_Entao_UsaWorkspaceRoot()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var home = Directory.CreateTempSubdirectory("taskboard-ws-tests-");
        var executablePath = Path.Combine(directory.FullName, "codex");
        File.WriteAllText(executablePath, "#!/bin/sh\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);
            var workspace = new Taskboard.Integrations.Workspace.WorkspaceService(
                null, home.FullName,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Taskboard.Integrations.Workspace.WorkspaceService>.Instance);
            var request = CriarRequest(AgentType.Codex) with { RepoPath = "" };

            var command = new KnownCliAgentAdapter(workspace).BuildCommand(request);

            command.WorkingDirectory.ShouldBe(Path.Combine(home.FullName, "repos"));
            Directory.Exists(command.WorkingDirectory).ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
            Directory.Delete(home.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dado_ExecutavelAusenteNoPath_Quando_MontarComando_Entao_LancaFileNotFoundException()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);

            Should.Throw<FileNotFoundException>(() =>
                new KnownCliAgentAdapter().BuildCommand(CriarRequest(AgentType.Codex)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static AgentExecutionRequest CriarRequest(AgentType agentType)
        => new(
            "issue-1",
            1,
            "owner/repo",
            "/workspace/repo",
            "feature/agents",
            "src/Agents",
            "Implementar a orquestração.",
            agentType);

    private static void SetExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }
}
