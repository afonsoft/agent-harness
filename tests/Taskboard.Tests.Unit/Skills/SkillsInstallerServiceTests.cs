using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Integrations.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Skills;

public class SkillsInstallerServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;
    private readonly string _homeDir;

    public SkillsInstallerServiceTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-install-{Guid.NewGuid()}");
        _dataDir = Path.Join(_root, "data");
        _homeDir = Path.Join(_root, "home");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_homeDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeRunner : ISkillsInstallRunner
    {
        public List<(string Exe, string Dir, IReadOnlyList<string> Args)> Calls { get; } = [];
        public Func<string, string, IReadOnlyList<string>, CommandResult>? Handler;
        public Func<Task>? OnFirstCall; // for coalescing tests

        public async Task<CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add((executable, workingDirectory, arguments));
            if (OnFirstCall is not null)
            {
                var hook = OnFirstCall;
                OnFirstCall = null;
                await hook();
            }

            return Handler?.Invoke(executable, workingDirectory, arguments)
                   ?? new CommandResult(0, string.Empty, string.Empty);
        }
    }

    private SkillsInstallerService CreateService(
        FakeRunner runner,
        Func<string, string?>? locator = null,
        IConfiguration? configuration = null,
        string? dataDir = null,
        string? homeDir = null) =>
        new(
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<SkillsInstallerService>.Instance,
            dataDir ?? _dataDir,
            homeDir ?? _homeDir,
            accessTokenProvider: null,
            runner: runner,
            executableLocator: locator ?? (name => "/usr/bin/" + name));

    private static CommandResult GitHandler(string dir, IReadOnlyList<string> args)
    {
        if (args.Count > 0 && args[0] == "remote")
        {
            return new CommandResult(0, string.Empty, string.Empty);
        }

        if (args.Count > 0 && args[0] == "clone")
        {
            // Simulate a successful clone: create the target with .git + install.sh.
            var target = args[^1];
            Directory.CreateDirectory(Path.Join(target, ".git"));
            File.WriteAllText(Path.Join(target, "install.sh"), "#!/bin/sh\nexit 0\n");
            return new CommandResult(0, string.Empty, string.Empty);
        }

        return new CommandResult(0, string.Empty, string.Empty);
    }

    [Fact]
    public async Task Dado_NpxAusente_Quando_Install_Entao_FalhaComPrerequisito()
    {
        // Covers RF-001: missing npx fails fast with PrerequisiteMissing
        var runner = new FakeRunner();
        var service = CreateService(runner, name => name == "npx" ? null : "/usr/bin/" + name);

        var status = await service.InstallAsync();

        status.State.ShouldBe(SkillsSyncState.Failed);
        status.Error.ShouldNotBeNull();
        status.Error.ShouldContain("PrerequisiteMissing");
        status.Error.ShouldContain("npx");
        runner.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_TudoOk_Quando_Install_Entao_ExecutaNpxEInstallSh()
    {
        // Covers RF-002/RF-003/RF-004: npx add -g --all --copy, then install.sh --all from the cache
        var runner = new FakeRunner
        {
            Handler = (exe, dir, args) => exe.EndsWith("git") ? GitHandler(dir, args) : new CommandResult(0, "", "")
        };
        var service = CreateService(runner);

        var status = await service.InstallAsync();

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        status.Repository.ShouldBe("afonsoft/skills");

        var npx = runner.Calls.Single(c => c.Exe.EndsWith("npx"));
        npx.Args.ShouldBe(["skills", "add", "afonsoft/skills", "-g", "--all", "--copy"]);

        var bash = runner.Calls.Single(c => c.Exe.EndsWith("bash"));
        bash.Args.ShouldBe(["install.sh", "--all"]);
        bash.Dir.ShouldBe(Path.Join(_dataDir, "skills-cache"));

        status.Steps.ShouldContain(s => s.Name == "npx-add" && s.State == SkillsInstallStepState.Succeeded);
        status.Steps.ShouldContain(s => s.Name == "install-sh" && s.State == SkillsInstallStepState.Succeeded);

        File.Exists(Path.Join(_dataDir, "skills-install.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_RepositorioSemInstallSh_Quando_Install_Entao_StepSkippedESucesso()
    {
        // Covers RF-003 edge: repo without install.sh → step Skipped, overall Succeeded
        var runner = new FakeRunner
        {
            Handler = (exe, dir, args) =>
            {
                if (exe.EndsWith("git") && args.Count > 0 && args[0] == "clone")
                {
                    var target = args[^1];
                    Directory.CreateDirectory(Path.Join(target, ".git"));
                    // no install.sh
                    return new CommandResult(0, "", "");
                }

                return exe.EndsWith("git") ? GitHandler(dir, args) : new CommandResult(0, "", "");
            }
        };
        var service = CreateService(runner);

        var status = await service.InstallAsync();

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        status.Steps.ShouldContain(s =>
            s.Name == "install-sh" && s.State == SkillsInstallStepState.Skipped);
        runner.Calls.ShouldNotContain(c => c.Exe.EndsWith("bash"));
    }

    [Fact]
    public async Task Dado_BashAusente_Quando_Install_Entao_InstallShSkipped()
    {
        // Covers RF-001 edge: bash missing only skips the install.sh step
        var runner = new FakeRunner
        {
            Handler = (exe, dir, args) => exe.EndsWith("git") ? GitHandler(dir, args) : new CommandResult(0, "", "")
        };
        var service = CreateService(runner, name => name == "bash" ? null : "/usr/bin/" + name);

        var status = await service.InstallAsync();

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        status.Steps.ShouldContain(s =>
            s.Name == "install-sh" && s.State == SkillsInstallStepState.Skipped);
    }

    [Fact]
    public async Task Dado_NpxFalha_Quando_Install_Entao_StatusFailed()
    {
        // Covers RF-008: non-zero npx exit → Failed with sanitized message
        var runner = new FakeRunner
        {
            Handler = (exe, dir, args) => exe.EndsWith("npx")
                ? new CommandResult(1, "", "npm error code ENOENT")
                : new CommandResult(0, "", "")
        };
        var service = CreateService(runner);

        var status = await service.InstallAsync();

        status.State.ShouldBe(SkillsSyncState.Failed);
        status.Steps.ShouldContain(s => s.Name == "npx-add" && s.State == SkillsInstallStepState.Failed);
        status.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_SkillsNoDiretorioGlobal_Quando_Verify_Entao_InstalledTrue()
    {
        // Covers RF-005: verify counts skill dirs with valid SKILL.md under global locations
        var skill = Path.Join(_homeDir, ".claude", "skills", "alpha");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Join(skill, "SKILL.md"), "---\nname: alpha\ndescription: test\n---\nbody\n");

        var service = CreateService(new FakeRunner());
        var status = await service.VerifyAsync();

        status.Installed.ShouldBeTrue();
        status.SkillCount.ShouldBe(1);
        status.Locations.ShouldContain(l => l.Count == 1);
    }

    [Fact]
    public async Task Dado_NadaInstalado_Quando_Verify_Entao_InstalledFalse()
    {
        // Covers RF-005: empty filesystem → not installed
        var service = CreateService(new FakeRunner());

        var status = await service.VerifyAsync();

        status.Installed.ShouldBeFalse();
        status.SkillCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_InstallEmAndamento_Quando_SegundoInstall_Entao_Coalesce()
    {
        // Covers RF-006: concurrent installs coalesce — no second process spawn
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner
        {
            OnFirstCall = () => gate.Task
        };
        var service = CreateService(runner);

        var first = service.InstallAsync();
        await Task.Delay(50);
        var second = await service.InstallAsync();

        second.State.ShouldBe(SkillsSyncState.Running);
        runner.Calls.Count.ShouldBe(1);

        gate.SetResult();
        await first;
    }

    [Fact]
    public void Dado_ManifestoCorrompido_Quando_GetStatus_Entao_NaoInstaladoSemExcecao()
    {
        // Covers RF-004 edge: corrupt manifest treated as never installed
        File.WriteAllText(Path.Join(_dataDir, "skills-install.json"), "{ not json !!!");
        var service = CreateService(new FakeRunner());

        var status = service.GetStatus();

        status.Installed.ShouldBeFalse();
        status.State.ShouldBe(SkillsSyncState.Idle);
    }

    [Fact]
    public async Task Dado_RepositorioConfigurado_Quando_Install_Entao_UsaRepositorio()
    {
        // Covers RF-002: repo comes from Taskboard:Skills:Repository
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Skills:Repository"] = "acme/custom-skills"
            })
            .Build();
        var runner = new FakeRunner
        {
            Handler = (exe, dir, args) => exe.EndsWith("git") ? GitHandler(dir, args) : new CommandResult(0, "", "")
        };
        var service = CreateService(runner, null, configuration, _dataDir, _homeDir);

        var status = await service.InstallAsync();

        status.Repository.ShouldBe("acme/custom-skills");
        var npx = runner.Calls.Single(c => c.Exe.EndsWith("npx"));
        npx.Args.ShouldContain("acme/custom-skills");
    }
}
