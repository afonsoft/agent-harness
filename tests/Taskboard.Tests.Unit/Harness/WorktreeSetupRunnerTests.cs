using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts P1: v2 yaml parse (setup/archive,
/// policy, tabs, issueCommand), config override precedence, sequential
/// runner semantics (stop-on-fail, scrub, tail, card lifecycle).
/// </summary>
public class WorktreeSetupRunnerTests : IDisposable
{
    private readonly string _repo = Path.Combine(
        Path.GetTempPath(), $"repo-{Guid.NewGuid():N}");
    private readonly string _worktree = Path.Combine(
        Path.GetTempPath(), $"wt-{Guid.NewGuid():N}");

    public WorktreeSetupRunnerTests()
    {
        Directory.CreateDirectory(_repo);
        Directory.CreateDirectory(_worktree);
    }

    public void Dispose()
    {
        Directory.Delete(_repo, recursive: true);
        Directory.Delete(_worktree, recursive: true);
    }

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private string WriteYamlFile(string yaml)
    {
        var file = Path.Join(_repo, ".harness", "worktree.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, yaml);
        return file;
    }

    // ---------- WorktreeYamlReader: v2 fields ----------

    [Fact]
    public void Dado_YamlV2_Quando_Read_Entao_SetupArchivePolicyTabsIssue()
    {
        WriteYamlFile("""
            shared: [node_modules]
            exclude: [secrets]
            setup:
              - npm ci
              - dotnet restore
            archive:
              - rm -rf .next
            agentStartupPolicy: start-immediately
            defaultTabs:
              - name: server
                command: npm run dev
            issueCommand: fix issue {issue}
            """);

        var doc = WorktreeYamlReader.Parse(_repo);

        doc.Setup.ShouldBe(["npm ci", "dotnet restore"]);
        doc.Archive.ShouldBe(["rm -rf .next"]);
        doc.AgentStartupPolicy.ShouldBe("start-immediately");
        doc.DefaultTabs.ShouldBe([new WorktreeYamlTab("server", "npm run dev")]);
        doc.IssueCommand.ShouldBe("fix issue {issue}");
        doc.Shared.ShouldBe([new WorktreeYamlSharedEntry("node_modules", null, false)]);
        doc.Exclude.ShouldBe(["secrets"]);
    }

    [Fact]
    public void Dado_SemYaml_Quando_Read_Entao_DocVazio()
    {
        var doc = WorktreeYamlReader.Parse(_repo);

        doc.Setup.ShouldBeEmpty();
        doc.Archive.ShouldBeEmpty();
        doc.AgentStartupPolicy.ShouldBeNull();
        doc.DefaultTabs.ShouldBeEmpty();
        doc.IssueCommand.ShouldBeNull();
    }

    [Fact]
    public void Dado_YamlQuebrado_Quando_Read_Entao_DocVazioSemThrow()
    {
        WriteYamlFile("setup: [unclosed\n  - : : :");

        var doc = WorktreeYamlReader.Parse(_repo);

        doc.Setup.ShouldBeEmpty();
        doc.Shared.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SetupComComandoLongoOuMultiline_Quando_Read_Entao_IgnoraInvalidos()
    {
        var longo = new string('x', WorktreeYamlReader.MaxCommandLength + 1);
        WriteYamlFile($"""
            setup:
              - echo ok
              - {longo}
              - "echo a\nb"
            """);

        var doc = WorktreeYamlReader.Parse(_repo);

        doc.Setup.ShouldBe(["echo ok"]);
    }

    [Fact]
    public void Dado_SetupAcimaDoLimite_Quando_Read_Entao_TruncaEm20()
    {
        WriteYamlFile($"setup:\n{string.Concat(Enumerable.Range(0, 25).Select(i => $"  - echo {i}\n"))}");

        var doc = WorktreeYamlReader.Parse(_repo);

        doc.Setup.Count.ShouldBe(WorktreeYamlReader.MaxCommands);
    }

    [Fact]
    public void Dado_ConfigSetup_Quando_EffectiveCommands_Entao_ConfigVenceYaml()
    {
        WriteYamlFile("setup: [echo yaml]");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Worktree:Setup:0"] = "echo config",
            })
            .Build();
        var reader = new WorktreeYamlReader(
            config, NullLogger<WorktreeYamlReader>.Instance);

        var doc = reader.Read(_repo);

        reader.EffectiveSetupCommands(_repo, doc).ShouldBe(["echo config"]);
    }

    [Fact]
    public void Dado_Politica_Quando_Effective_Entao_ConfigVenceYamlVenceDefault()
    {
        WriteYamlFile("agentStartupPolicy: start-immediately");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Worktree:SetupPolicy"] = "wait-for-setup",
            })
            .Build();
        var reader = new WorktreeYamlReader(
            config, NullLogger<WorktreeYamlReader>.Instance);
        var doc = reader.Read(_repo);

        reader.EffectiveStartupPolicy(_repo, doc)
            .ShouldBe(WorktreeSetupPolicies.WaitForSetup);

        reader = new WorktreeYamlReader(
            EmptyConfig(), NullLogger<WorktreeYamlReader>.Instance);
        reader.EffectiveStartupPolicy(_repo, doc)
            .ShouldBe(WorktreeSetupPolicies.StartImmediately);

        WriteYamlFile("agentStartupPolicy: nonsense");
        reader.EffectiveStartupPolicy(_repo, reader.Read(_repo))
            .ShouldBe(WorktreeSetupPolicies.WaitForSetup);
    }

    [Fact]
    public void Dado_TabInvalida_Quando_Read_Entao_SomenteValidas()
    {
        WriteYamlFile("""
            defaultTabs:
              - name: ok
                command: echo hi
              - name: sem-comando
              - command: echo sem-nome
            """);

        WorktreeYamlReader.Parse(_repo).DefaultTabs
            .ShouldBe([new WorktreeYamlTab("ok", "echo hi")]);
    }

    // ---------- WorktreeSetupRunner ----------

    private (WorktreeSetupRunner Runner, StubRunner Proc, FakeCardService Cards) Sut()
    {
        var proc = new StubRunner();
        var cards = new FakeCardService();
        var services = new ServiceCollection()
            .AddSingleton<IWorktreeCardService>(cards)
            .BuildServiceProvider();
        var runner = new WorktreeSetupRunner(
            proc,
            EmptyConfig(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorktreeSetupRunner>.Instance);
        return (runner, proc, cards);
    }

    [Fact]
    public async Task Dado_ComandosOk_Quando_RunSetup_Entao_CardRunningDepoisOk()
    {
        var (runner, proc, cards) = Sut();

        var result = await runner.RunSetupAsync(
            _repo, _worktree, ["echo one", "echo two"]);

        result.State.ShouldBe(WorktreeSetupStates.Ok);
        result.ExitCode.ShouldBeNull();
        proc.Calls.Count.ShouldBe(2);
        proc.Calls[0].ShouldBe(["-lc", "echo one"]);
        proc.Calls[1].ShouldBe(["-lc", "echo two"]);
        cards.Transitions.ShouldBe([WorktreeSetupStates.Running, WorktreeSetupStates.Ok]);
        cards.LastLog!.ShouldContain("$ echo one");
    }

    [Fact]
    public async Task Dado_ComandoFalha_Quando_RunSetup_Entao_ParaEEMarcaFailed()
    {
        var (runner, proc, cards) = Sut();
        proc.NextExitCode = 3;

        var result = await runner.RunSetupAsync(
            _repo, _worktree, ["fail", "never-runs"]);

        result.State.ShouldBe(WorktreeSetupStates.Failed);
        result.ExitCode.ShouldBe(3);
        proc.Calls.Count.ShouldBe(1);
        cards.Transitions.ShouldBe([WorktreeSetupStates.Running, WorktreeSetupStates.Failed]);
        cards.LastExitCode.ShouldBe(3);
        result.LogTail.ShouldContain("exit code 3");
    }

    [Fact]
    public async Task Dado_OutputComSecret_Quando_RunSetup_Entao_LogScrubbed()
    {
        var (runner, proc, cards) = Sut();
        proc.NextStdOut = "token=ghp_abcdefghijklmnopqrstuvwxyz1234567890\n";

        var result = await runner.RunSetupAsync(_repo, _worktree, ["leak"]);

        result.LogTail.ShouldNotContain("ghp_");
        cards.LastLog!.ShouldNotContain("ghp_");
    }

    [Fact]
    public async Task Dado_LogGigante_Quando_RunSetup_Entao_Tail64KBPreamble4KB()
    {
        var (runner, proc, _) = Sut();
        proc.NextStdOut = new string('a', 200 * 1024);

        var result = await runner.RunSetupAsync(_repo, _worktree, ["flood"]);

        result.LogTail.Length.ShouldBeLessThanOrEqualTo(64 * 1024);
        WorktreeSetupRunner.PreambleTail(result.LogTail).Length
            .ShouldBeLessThanOrEqualTo(4 * 1024);
    }

    private sealed class StubRunner : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int NextExitCode { get; set; }
        public string NextStdOut { get; set; } = "";

        public Task<ProcessRunResult> RunAsync(
            string executable, string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            return Task.FromResult(
                new ProcessRunResult(NextExitCode, NextStdOut, "", false));
        }
    }

    private sealed class FakeCardService : IWorktreeCardService
    {
        public List<string> Transitions { get; } = [];
        public string? LastLog { get; private set; }
        public int? LastExitCode { get; private set; }

        public Task<WorktreeCardDto> SetSetupAsync(
            string worktreePath, string repoPath, string state,
            int? exitCode, string? logTail, CancellationToken ct = default)
        {
            Transitions.Add(state);
            LastLog = logTail;
            LastExitCode = exitCode;
            return Task.FromResult(new WorktreeCardDto(
                worktreePath, repoPath, null, WorkspaceStatus.Unknown, null,
                DateTime.UtcNow, DateTime.UtcNow, state, exitCode,
                null, null, logTail));
        }

        public Task<WorktreeCardDto?> GetAsync(string worktreePath, CancellationToken ct = default) =>
            Task.FromResult<WorktreeCardDto?>(null);

        public Task<IReadOnlyList<WorktreeCardDto>> ListAsync(
            string? repoPath = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorktreeCardDto>>([]);

        public Task<WorktreeCardDto> SetCardAsync(
            string worktreePath, string? comment, string? status,
            string? taskId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-003: domain transitions of
/// <see cref="Taskboard.Domain.Entities.Harness.WorktreeCard.SetSetup"/> —
/// running stamps start, terminal stamps finish + exit, pending stamps none.
/// </summary>
public class WorktreeCardSetupTests
{
    private static Taskboard.Domain.Entities.Harness.WorktreeCard Card() =>
        Taskboard.Domain.Entities.Harness.WorktreeCard.Create(
            "/wt", "/repo", null, WorkspaceStatus.Unknown, null,
            DateTime.UtcNow);

    [Fact]
    public void Dado_CardNovo_Quando_Default_Entao_SetupSkipped()
    {
        Card().SetupState.ShouldBe(WorktreeSetupStates.Skipped);
    }

    [Fact]
    public void Dado_Running_Quando_SetSetup_Entao_CarimbaStartSemFinish()
    {
        var card = Card();
        var now = DateTime.UtcNow;

        card.SetSetup(WorktreeSetupStates.Running, null, null, now);

        card.SetupState.ShouldBe(WorktreeSetupStates.Running);
        card.SetupStartedAtUtc.ShouldBe(now);
        card.SetupFinishedAtUtc.ShouldBeNull();
        card.SetupExitCode.ShouldBeNull();
    }

    [Fact]
    public void Dado_Running_Quando_TerminaOk_Entao_CarimbaFinishComLog()
    {
        var card = Card();
        var start = DateTime.UtcNow;
        card.SetSetup(WorktreeSetupStates.Running, null, null, start);

        var end = start.AddSeconds(42);
        card.SetSetup(WorktreeSetupStates.Ok, 0, "done", end);

        card.SetupState.ShouldBe(WorktreeSetupStates.Ok);
        card.SetupStartedAtUtc.ShouldBe(start);
        card.SetupFinishedAtUtc.ShouldBe(end);
        card.SetupExitCode.ShouldBe(0);
        card.SetupLog.ShouldBe("done");
    }

    [Fact]
    public void Dado_TerminalSemRunning_Quando_SetSetup_Entao_StartRecebeNow()
    {
        var card = Card();
        var now = DateTime.UtcNow;

        card.SetSetup(WorktreeSetupStates.Failed, 7, "boom", now);

        card.SetupStartedAtUtc.ShouldBe(now);
        card.SetupFinishedAtUtc.ShouldBe(now);
        card.SetupExitCode.ShouldBe(7);
    }

    [Fact]
    public void Dado_Pending_Quando_SetSetup_Entao_SemTimestamps()
    {
        var card = Card();

        card.SetSetup(WorktreeSetupStates.Pending, null, null, DateTime.UtcNow);

        card.SetupState.ShouldBe(WorktreeSetupStates.Pending);
        card.SetupStartedAtUtc.ShouldBeNull();
        card.SetupFinishedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Dado_EstadoInvalido_Quando_SetSetup_Entao_InvalidValue()
    {
        var ex = Should.Throw<DomainException>(
            () => Card().SetSetup("bogus", null, null, DateTime.UtcNow));

        ex.Message.ShouldContain("pending|running|ok|failed|skipped");
    }
}

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-007 / P3: issueCommand template —
/// variáveis substituídas; template perigoso (`{{`, `}}`, `$(`, placeholder
/// desconhecido) rejeitado para o caller cair no prompt default.
/// </summary>
public class IssueCommandTemplateTests
{
    [Fact]
    public void Dado_TemplateComVariaveis_Quando_Render_Entao_Substitui()
    {
        var rendered = IssueCommandTemplate.Render(
            "Fix {issueKey}: {issueTitle} (repo {repo}, n {issueNumber})",
            "afonsoft/agent-harness", 42, "Login broken");

        rendered.ShouldBe(
            "Fix afonsoft/agent-harness#42: Login broken (repo afonsoft/agent-harness, n 42)");
    }

    [Fact]
    public void Dado_IssueUrlEBody_Quando_Render_Entao_SubstituiTodos()
    {
        var rendered = IssueCommandTemplate.Render(
            "Open {issueUrl} — {issueBody}",
            "o/r", 7, "t", "the body", "https://github.com/o/r/issues/7");

        rendered.ShouldBe("Open https://github.com/o/r/issues/7 — the body");
    }

    [Theory]
    [InlineData("run {{cmd}} now")]                 // escape sintaxe estrangeira
    [InlineData("close }} braces")]                  // }} solto
    [InlineData("echo $(whoami)")]                   // shell substitution
    [InlineData("fix {unknownVar}")]                 // placeholder desconhecido
    [InlineData("brace { solto")]                    // { não pareado
    public void Dado_TemplatePerigoso_Quando_Render_Entao_Rejeita(string template)
    {
        IssueCommandTemplate.Render(template, "o/r", 1, "t").ShouldBeNull();
    }

    [Fact]
    public void Dado_TemplateVazio_Quando_Render_Entao_Rejeita()
    {
        IssueCommandTemplate.Render("   ", "o/r", 1, "t").ShouldBeNull();
    }
}
