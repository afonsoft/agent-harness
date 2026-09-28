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

            // SPEC-20260928-ai-code-generic-cli RF-001: all 14 AgentType are
            // discovered — not only the 6 originally mapped.
            agents.Count.ShouldBe(Enum.GetValues<AgentType>().Length);
            var claude = agents.Single(agent => agent.Type == AgentType.Claude);
            claude.Status.ShouldBe(AgentStatus.Available);
            claude.ExecutablePath.ShouldBe(executablePath);
            // Versions come from the probe snapshot — none here.
            claude.Version.ShouldBeNull();
            claude.Transport.ShouldBe("acp");

            agents
                .Where(agent => agent.Type != AgentType.Claude)
                .ShouldAllBe(agent => agent.Status == AgentStatus.Unavailable && agent.ExecutablePath == string.Empty);
            // Non-ACP builtins advertise the pty transport.
            agents.Single(a => a.Type == AgentType.Aider).Transport.ShouldBe("pty");
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
