using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Skills;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20260928-ai-code-generic-cli RF-004: docker ps/exec parsing — nunca sobe erro para o request.</summary>
public class DockerCliDiscoveryTests
{
    private sealed class FakeRunner : ISkillsInstallRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Func<IReadOnlyList<string>, CommandResult>? Handler;

        public Task<CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(
                Handler?.Invoke(arguments)
                ?? new CommandResult(0, string.Empty, string.Empty));
        }
    }

    private static DockerCliDiscovery Create(
        Func<string, string?>? locator = null, FakeRunner? runner = null) =>
        new(
            Path.GetTempPath(),
            NullLogger<DockerCliDiscovery>.Instance,
            executableLocator: locator ?? (_ => "/usr/bin/docker"),
            runner: runner ?? new FakeRunner());

    [Fact]
    public async Task Dado_SemDockerNoPath_Quando_IsAvailable_Entao_False()
    {
        var sut = Create(locator: _ => null);

        (await sut.IsAvailableAsync()).ShouldBeFalse();
        (await sut.ListContainersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_DockerInfoFalha_Quando_IsAvailable_Entao_False()
    {
        var runner = new FakeRunner
        {
            Handler = _ => new CommandResult(1, string.Empty, "daemon not running"),
        };
        var sut = Create(runner: runner);

        (await sut.IsAvailableAsync()).ShouldBeFalse();
        (await sut.ListContainersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ContainersRodando_Quando_Lista_Entao_ProbaClisPorContainer()
    {
        var runner = new FakeRunner
        {
            Handler = args => args switch
            {
                _ when args.Contains("info") => new CommandResult(0, "27.0", string.Empty),
                _ when args.Contains("ps") => new CommandResult(
                    0, "dev|ubuntu:24.04\nother|debian:latest\n", string.Empty),
                // `which claude` found only in "dev"; nothing in "other".
                _ when args.Contains("dev") && args.Contains("claude") => new CommandResult(0, "/usr/bin/claude", string.Empty),
                _ => new CommandResult(1, string.Empty, string.Empty),
            },
        };
        var sut = Create(runner: runner);

        var containers = await sut.ListContainersAsync();

        containers.Count.ShouldBe(2);
        containers[0].Name.ShouldBe("dev");
        containers[0].Image.ShouldBe("ubuntu:24.04");
        containers[0].AvailableClis.ShouldContain("claude");
        containers[1].Name.ShouldBe("other");
        containers[1].AvailableClis.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_DockerPsFalha_Quando_Lista_Entao_VazioSemExcecao()
    {
        var runner = new FakeRunner
        {
            Handler = args => args.Contains("info")
                ? new CommandResult(0, "27.0", string.Empty)
                : new CommandResult(1, string.Empty, "boom"),
        };
        var sut = Create(runner: runner);

        var containers = await sut.ListContainersAsync();

        containers.ShouldBeEmpty();
    }
}
