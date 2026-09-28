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

    private CliProbeSnapshotService CreateSnapshot(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null) =>
        new(
            Path.Join(Path.GetTempPath(), $"tb-models-snap-{Guid.NewGuid()}.json"),
            _homeDir,
            NullLogger<CliProbeSnapshotService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner());

    private AgentModelCatalogService CreateService(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null,
        CliProbeSnapshotService? snapshot = null) =>
        new(
            _homeDir,
            NullLogger<AgentModelCatalogService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner(),
            snapshot: snapshot);

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
    public async Task Dado_OpenCodeInstalado_Quando_ForceRefresh_Entao_RodaModelsEParsa()
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

        var models = await service.ListAvailableAsync(AgentType.OpenCode, forceRefresh: true);

        models.ShouldBe(["opencode/big-pickle", "opencode/claude-sonnet-5"]);
        runner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SnapshotPopulado_Quando_ListAvailable_Entao_UsaSnapshotSemReexecutar()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, "opencode/m1\n", string.Empty),
        };
        Func<string, string?> locator = _ => "/usr/bin/opencode";
        var snapshot = CreateSnapshot(locator, runner);
        var service = CreateService(locator: locator, runner: runner, snapshot: snapshot);

        // Forced probe populates the snapshot…
        await service.ListAvailableAsync(AgentType.OpenCode, forceRefresh: true);
        // …the non-forced read is served instantly from it.
        var models = await service.ListAvailableAsync(AgentType.OpenCode);

        models.ShouldBe(["opencode/m1"]);
        runner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SnapshotVazio_Quando_ListAvailable_Entao_RetornaInstantaneoEDisparaWarmup()
    {
        var snapshot = CreateSnapshot(locator: _ => "/usr/bin/opencode", runner: new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, "opencode/m1\n", string.Empty),
        });
        var service = CreateService(locator: _ => "/usr/bin/opencode", snapshot: snapshot);

        var models = await service.ListAvailableAsync(AgentType.OpenCode);

        // SPEC-20260928: cold snapshot → instant empty answer + background warm.
        models.ShouldBeEmpty();
        await snapshot.RefreshAsync();
        (await service.ListAvailableAsync(AgentType.OpenCode)).ShouldBe(["opencode/m1"]);
    }

    [Fact]
    public async Task Dado_ForceRefresh_Quando_ListAvailable_Entao_IgnoraSnapshotEReexecuta()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, "opencode/m1\n", string.Empty),
        };
        Func<string, string?> locator = _ => "/usr/bin/opencode";
        var service = CreateService(locator: locator, runner: runner, snapshot: CreateSnapshot(locator, runner));

        await service.ListAvailableAsync(AgentType.OpenCode, forceRefresh: true);
        await service.ListAvailableAsync(AgentType.OpenCode, forceRefresh: true);

        runner.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_ProbeFalha_Quando_ForceRefresh_Entao_VazioSemLancar()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => throw new InvalidOperationException("boom"),
        };
        var service = CreateService(locator: _ => "/usr/bin/agy", runner: runner);

        var models = await service.ListAvailableAsync(AgentType.Antigravity, forceRefresh: true);

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
