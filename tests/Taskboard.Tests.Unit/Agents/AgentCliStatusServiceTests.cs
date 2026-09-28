using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentCliStatusServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _homeDir;

    public AgentCliStatusServiceTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-clis-{Guid.NewGuid()}");
        _homeDir = Path.Join(_root, "home");
        Directory.CreateDirectory(_homeDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeRunner : ISkillsInstallRunner
    {
        public Func<string, IReadOnlyList<string>, CommandResult>? Handler;

        public Task<CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Handler?.Invoke(executable, arguments)
                ?? new CommandResult(0, string.Empty, string.Empty));
    }

    private CliProbeSnapshotService CreateSnapshot(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null) =>
        new(
            Path.Join(_root, $"snapshot-{Guid.NewGuid()}.json"),
            _homeDir,
            NullLogger<CliProbeSnapshotService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner());

    private AgentCliStatusService CreateService(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null,
        CliProbeSnapshotService? snapshot = null,
        string? homeDir = null) =>
        new(
            homeDir ?? _homeDir,
            NullLogger<AgentCliStatusService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner(),
            snapshot: snapshot,
            refreshTtl: TimeSpan.Zero);

    [Fact]
    public async Task Dado_NenhumCliInstalado_Quando_GetStatus_Entao_TodosNaoInstalados()
    {
        var service = CreateService();

        var statuses = await service.GetStatusAsync();

        statuses.Count.ShouldBe(Enum.GetValues<AgentCliKind>().Length);
        statuses.ShouldAllBe(s => !s.Installed && s.Version == null && s.AuthStatus == AgentCliAuthStatus.Unknown);
    }

    [Fact]
    public async Task Dado_CliInstaladoComCredencial_Quando_GetStatus_Entao_Autenticado()
    {
        // Claude credential probe: ~/.claude/.credentials.json
        var credentialPath = Path.Join(_homeDir, ".claude", ".credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(credentialPath)!);
        File.WriteAllText(credentialPath, "{}");

        Func<string, string?> locator = name => name == "claude" ? "/usr/bin/claude" : null;
        var runner = new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, "claude 1.2.3 (build)\n", string.Empty)
        };
        // SPEC-20260928: versions live in the snapshot — refresh it first so
        // the status read stays instant and version comes pre-populated.
        var snapshot = CreateSnapshot(locator, runner);
        await snapshot.RefreshAsync();
        var service = CreateService(locator: locator, runner: runner, snapshot: snapshot);

        var statuses = await service.GetStatusAsync();
        var claude = statuses.Single(s => s.Agent == AgentCliKind.Claude);

        claude.Installed.ShouldBeTrue();
        claude.Version.ShouldBe("1.2.3");
        claude.AuthStatus.ShouldBe(AgentCliAuthStatus.Authenticated);
        claude.LoginCommand.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_SnapshotNaoRefreshed_Quando_GetStatus_Entao_VersaoNulaERespostaInstantanea()
    {
        var service = CreateService(locator: name => name == "claude" ? "/usr/bin/claude" : null);

        var statuses = await service.GetStatusAsync();
        var claude = statuses.Single(s => s.Agent == AgentCliKind.Claude);

        // Fast path: installed/auth are live; version waits for the snapshot.
        claude.Installed.ShouldBeTrue();
        claude.Version.ShouldBeNull();
        claude.AuthStatus.ShouldBe(AgentCliAuthStatus.NotAuthenticated);
    }

    [Fact]
    public async Task Dado_CliInstaladoSemCredencial_Quando_GetStatus_Entao_NaoAutenticado()
    {
        var service = CreateService(locator: name => name == "codex" ? "/usr/bin/codex" : null);

        var statuses = await service.GetStatusAsync();
        var codex = statuses.Single(s => s.Agent == AgentCliKind.Codex);

        codex.Installed.ShouldBeTrue();
        codex.AuthStatus.ShouldBe(AgentCliAuthStatus.NotAuthenticated);
    }

    [Fact]
    public async Task Dado_ProbeDeVersaoFalha_Quando_GetStatus_Entao_VersaoNulaSemErro()
    {
        Func<string, string?> locator = name => name == "devin" ? "/usr/bin/devin" : null;
        var runner = new FakeRunner
        {
            Handler = (_, _) => throw new InvalidOperationException("boom")
        };
        var snapshot = CreateSnapshot(locator, runner);
        await snapshot.RefreshAsync();
        var service = CreateService(locator: locator, runner: runner, snapshot: snapshot);

        var statuses = await service.GetStatusAsync();
        var devin = statuses.Single(s => s.Agent == AgentCliKind.Devin);

        devin.Installed.ShouldBeTrue();
        devin.Version.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_SaidaDeVersaoInvalida_Quando_GetStatus_Entao_UsaPrimeiraLinhaLimitada()
    {
        Func<string, string?> locator = name => name == "agy" ? "/usr/bin/agy" : null;
        var runner = new FakeRunner
        {
            Handler = (_, _) => new CommandResult(0, new string('x', 100) + "\n", string.Empty)
        };
        var snapshot = CreateSnapshot(locator, runner);
        await snapshot.RefreshAsync();
        var service = CreateService(locator: locator, runner: runner, snapshot: snapshot);

        var statuses = await service.GetStatusAsync();
        var agy = statuses.Single(s => s.Agent == AgentCliKind.Antigravity);

        agy.Version.ShouldNotBeNull();
        agy.Version!.Length.ShouldBeLessThanOrEqualTo(40);
    }
}
