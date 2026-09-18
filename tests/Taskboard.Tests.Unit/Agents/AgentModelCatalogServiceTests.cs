using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Skills;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentModelCatalogServiceTests
{
    private readonly string _homeDir = Path.GetTempPath();

    private sealed class FakeRunner : ISkillsInstallRunner
    {
        public int Calls;
        public Func<string, IReadOnlyList<string>, CommandResult>? Handler;

        public Task<CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(
                Handler?.Invoke(executable, arguments)
                ?? new CommandResult(0, string.Empty, string.Empty));
        }
    }

    private AgentModelCatalogService CreateService(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null) =>
        new(
            _homeDir,
            NullLogger<AgentModelCatalogService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner());

    [Fact]
    public async Task Dado_CliSemProbe_Quando_ListAvailable_Entao_VazioSemExecutarProcesso()
    {
        var runner = new FakeRunner();
        var service = CreateService(locator: _ => "/usr/bin/claude", runner: runner);

        // Claude supports --model but has no headless model-list command.
        var models = await service.ListAvailableAsync(AgentType.Claude);

        models.ShouldBeEmpty();
        runner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_CliComProbeNaoInstalado_Quando_ListAvailable_Entao_Vazio()
    {
        var runner = new FakeRunner();
        var service = CreateService(locator: _ => null, runner: runner);

        var models = await service.ListAvailableAsync(AgentType.OpenCode);

        models.ShouldBeEmpty();
        runner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_OpenCodeInstalado_Quando_ListAvailable_Entao_RodaModelsEParsa()
    {
        var runner = new FakeRunner
        {
            Handler = (_, args) =>
            {
                args.ShouldBe(["models"]);
                return new CommandResult(0, "opencode/big-pickle\nopencode/claude-sonnet-5\n", string.Empty);
            },
        };
        var service = CreateService(locator: name => name == "opencode" ? "/usr/bin/opencode" : null, runner: runner);

        var models = await service.ListAvailableAsync(AgentType.OpenCode);

        models.ShouldBe(["opencode/big-pickle", "opencode/claude-sonnet-5"]);
        runner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SegundaChamada_Quando_ListAvailable_Entao_UsaCache()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, "opencode/m1\n", string.Empty),
        };
        var service = CreateService(locator: _ => "/usr/bin/opencode", runner: runner);

        await service.ListAvailableAsync(AgentType.OpenCode);
        var models = await service.ListAvailableAsync(AgentType.OpenCode);

        models.ShouldBe(["opencode/m1"]);
        runner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_ProbeFalha_Quando_ListAvailable_Entao_VazioSemLancar()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => throw new InvalidOperationException("boom"),
        };
        var service = CreateService(locator: _ => "/usr/bin/agy", runner: runner);

        var models = await service.ListAvailableAsync(AgentType.Antigravity);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_CliGerenciado_Quando_ListAvailable_Entao_Vazio()
    {
        var service = CreateService(locator: _ => "/usr/bin/cline");

        var models = await service.ListAvailableAsync(AgentType.Cline);

        models.ShouldBeEmpty();
    }
}
