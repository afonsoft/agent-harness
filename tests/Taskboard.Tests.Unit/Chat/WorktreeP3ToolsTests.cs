using System.Text.Json;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts P3 (RF-007/RF-008): worktree_status
/// consolida session+card+tabs+probe; worktree_setup re-executa o setup
/// declarado e propaga o estado para a sessão.
/// </summary>
public class WorktreeP3ToolsTests : IDisposable
{
    private readonly string _worktreeDir = Path.Join(
        Path.GetTempPath(), $"wt-p3-{Guid.NewGuid():N}");

    public WorktreeP3ToolsTests() => Directory.CreateDirectory(_worktreeDir);

    private static JsonElement Args(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    private static WorktreeSessionDto Session(string runId, string worktreePath) =>
        new("sid", runId, worktreePath, "feature/x", "Active", "/repo", "main",
            null, false, DateTime.UtcNow, DateTime.UtcNow, 1,
            ["node_modules"], WorktreeSetupStates.Ok, "setup log");

    // ---- worktree_status (RF-008) ----

    [Fact]
    public async Task Dado_RunId_Quando_Status_Entao_ConsolidaSessionCardTabs()
    {
        var sessions = Substitute.For<IWorktreeSessionRepository>();
        sessions.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Session("run-1", _worktreeDir));
        var cards = Substitute.For<IWorktreeCardService>();
        cards.GetAsync(_worktreeDir, Arg.Any<CancellationToken>())
            .Returns(new WorktreeCardDto(
                _worktreeDir, "/repo", "wip", WorkspaceStatus.Dirty, "t1",
                DateTime.UtcNow, DateTime.UtcNow,
                SetupState: WorktreeSetupStates.Ok, SetupLogTail: "tail"));
        var probe = Substitute.For<IWorktreeStatusProbe>();
        var yaml = Substitute.For<IWorktreeYamlReader>();
        yaml.Read("/repo").Returns(new WorktreeYamlDoc(
            [], [], [], [], null,
            [new WorktreeYamlTab("logs", "tail -f x")], null));
        var tool = new WorktreeStatusTool(
            TestScopeFactory.WithWorktree(sessions, cards, probe, yaml));

        var result = await tool.ExecuteAsync(
            Args("""{"runId":"run-1"}"""), TestScopeFactory.Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        var root = doc.RootElement;
        root.GetProperty("setupState").GetString().ShouldBe(WorktreeSetupStates.Ok);
        root.GetProperty("setupLogTail").GetString().ShouldBe("tail");
        root.GetProperty("workspaceStatus").GetString().ShouldBe("Dirty");
        root.GetProperty("sharedPathsApplied")[0].GetString().ShouldBe("node_modules");
        root.GetProperty("defaultTabs")[0].GetProperty("name").GetString().ShouldBe("logs");
    }

    [Fact]
    public async Task Dado_WorktreePath_Quando_Status_Entao_RepoPathViaProbe()
    {
        var sessions = Substitute.For<IWorktreeSessionRepository>();
        var cards = Substitute.For<IWorktreeCardService>();
        cards.GetAsync(_worktreeDir, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorktreeCardDto?>(null));
        var probe = Substitute.For<IWorktreeStatusProbe>();
        probe.ResolveRepoPathAsync(_worktreeDir, Arg.Any<CancellationToken>())
            .Returns("/repo");
        probe.ProbeAsync(_worktreeDir, Arg.Any<CancellationToken>())
            .Returns(WorkspaceStatus.Clean);
        var yaml = Substitute.For<IWorktreeYamlReader>();
        yaml.Read("/repo").Returns(new WorktreeYamlDoc([], [], [], [], null, [], null));
        var tool = new WorktreeStatusTool(
            TestScopeFactory.WithWorktree(sessions, cards, probe, yaml));

        var result = await tool.ExecuteAsync(
            Args($$"""{"worktreePath":"{{_worktreeDir}}"}"""),
            TestScopeFactory.Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("workspaceStatus").GetString().ShouldBe("Clean");
        doc.RootElement.GetProperty("repoPath").GetString().ShouldBe("/repo");
    }

    [Fact]
    public async Task Dado_SemIdentificador_Quando_Status_Entao_Erro()
    {
        var tool = new WorktreeStatusTool(TestScopeFactory.WithWorktree(
            Substitute.For<IWorktreeSessionRepository>(),
            Substitute.For<IWorktreeCardService>(),
            Substitute.For<IWorktreeStatusProbe>()));

        var result = await tool.ExecuteAsync(
            Args("{}"), TestScopeFactory.Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- worktree_setup (rerun manual) ----

    [Fact]
    public async Task Dado_SetupDeclarado_Quando_Rerun_Entao_SessaoAtualizada()
    {
        var sessions = Substitute.For<IWorktreeSessionRepository>();
        sessions.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Session("run-1", _worktreeDir));
        var cards = Substitute.For<IWorktreeCardService>();
        var probe = Substitute.For<IWorktreeStatusProbe>();
        var runner = Substitute.For<IWorktreeSetupRunner>();
        runner.RerunForSessionAsync("run-1", Arg.Any<WorktreeSessionDto>(), Arg.Any<CancellationToken>())
            .Returns(new WorktreeSetupRerunResult(
                new WorktreeSetupResult(
                    WorktreeSetupStates.Ok, null, "log", DateTime.UtcNow, DateTime.UtcNow),
                Session("run-1", _worktreeDir)));
        var tool = new WorktreeSetupRerunTool(
            TestScopeFactory.WithWorktree(
                sessions, cards, probe,
                Substitute.For<IWorktreeYamlReader>(), runner));

        var result = await tool.ExecuteAsync(
            Args("""{"runId":"run-1"}"""), TestScopeFactory.Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        await runner.Received(1).RerunForSessionAsync(
            "run-1", Arg.Any<WorktreeSessionDto>(), Arg.Any<CancellationToken>());
        using var parsed = JsonDocument.Parse(result.Json);
        parsed.RootElement.GetProperty("setupState").GetString().ShouldBe(WorktreeSetupStates.Ok);
    }

    [Fact]
    public async Task Dado_SemSetup_Quando_Rerun_Entao_Erro()
    {
        var sessions = Substitute.For<IWorktreeSessionRepository>();
        sessions.GetByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(Session("run-1", _worktreeDir));
        var runner = Substitute.For<IWorktreeSetupRunner>();
        runner.RerunForSessionAsync("run-1", Arg.Any<WorktreeSessionDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorktreeSetupRerunResult>>(_ =>
                throw new DomainException("invalid", "No setup commands declared for this worktree."));
        var tool = new WorktreeSetupRerunTool(TestScopeFactory.WithWorktree(
            sessions, Substitute.For<IWorktreeCardService>(),
            Substitute.For<IWorktreeStatusProbe>(),
            Substitute.For<IWorktreeYamlReader>(), runner));

        var result = await tool.ExecuteAsync(
            Args("""{"runId":"run-1"}"""), TestScopeFactory.Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_worktreeDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
