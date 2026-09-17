using System.Runtime.InteropServices;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

[Collection("PathEnvironment")]
public class AgentDiscoveryServiceTests
{
    [Fact]
    public async Task Dado_ApenasClaudeNoPath_Quando_DescobrirAgentes_Entao_RetornaDisponibilidadeEVersaoCorretas()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var executablePath = Path.Combine(directory.FullName, "claude");
        File.WriteAllText(executablePath, "#!/bin/sh\necho claude 1.2.3\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);

            var agents = await new AgentDiscoveryService().DiscoverAsync();

            agents.Count.ShouldBe(6);
            var claude = agents.Single(agent => agent.Type == AgentType.Claude);
            claude.Status.ShouldBe(AgentStatus.Available);
            claude.Version.ShouldBe("claude 1.2.3");
            claude.ExecutablePath.ShouldBe(executablePath);

            agents
                .Where(agent => agent.Type != AgentType.Claude)
                .ShouldAllBe(agent => agent.Status == AgentStatus.Unavailable && agent.ExecutablePath == string.Empty);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dado_ClaudeNoPath_Quando_ResolverExecutavel_Entao_RetornaCaminhoDoExecutavel()
    {
        var directory = Directory.CreateTempSubdirectory("taskboard-agent-tests-");
        var executablePath = Path.Combine(directory.FullName, "claude");
        File.WriteAllText(executablePath, "#!/bin/sh\necho claude 1.2.3\n");
        SetExecutable(executablePath);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.FullName);

            new AgentDiscoveryService()
                .ResolveExecutablePath(AgentType.Claude)
                .ShouldBe(executablePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

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
