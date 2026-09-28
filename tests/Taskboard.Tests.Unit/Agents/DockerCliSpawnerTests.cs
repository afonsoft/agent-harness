using Shouldly;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20260928-ai-code-generic-cli RF-004/RF-005: docker exec argv + name validation.</summary>
public class DockerCliSpawnerTests
{
    [Fact]
    public void Dado_Interativo_Quando_BuildExecArgs_Entao_ExecItContainerCmd()
    {
        var args = DockerCliSpawner.BuildExecArgs("dev", ["claude", "--acp"], interactive: true);

        args.ShouldBe(["exec", "-it", "dev", "claude", "--acp"]);
    }

    [Fact]
    public void Dado_NaoInterativo_Quando_BuildExecArgs_Entao_ExecIContainerCmd()
    {
        var args = DockerCliSpawner.BuildExecArgs("dev", ["mycli"], interactive: false);

        args.ShouldBe(["exec", "-i", "dev", "mycli"]);
    }

    [Fact]
    public void Dado_ArgsComEspacos_Quando_BuildExecArgs_Entao_NaoInterpola()
    {
        // Args go through as a single argv element — docker receives them
        // verbatim; no shell joins the command.
        var args = DockerCliSpawner.BuildExecArgs("dev", ["sh", "-c", "echo hi; rm -rf /"], interactive: true);

        args.ShouldBe(["exec", "-it", "dev", "sh", "-c", "echo hi; rm -rf /"]);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("my-container_1.2")]
    [InlineData("registry/ns")]
    public void Dado_NomeValido_Quando_IsValidContainerName_Entao_True(string name) =>
        DockerCliSpawner.IsValidContainerName(name).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dev; rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("dev`id`")]
    [InlineData("-starts-with-dash")]
    public void Dado_NomeInvalido_Quando_IsValidContainerName_Entao_False(string? name) =>
        DockerCliSpawner.IsValidContainerName(name).ShouldBeFalse();
}
