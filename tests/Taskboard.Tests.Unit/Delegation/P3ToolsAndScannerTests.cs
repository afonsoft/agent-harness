using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261006: agent_decide + worktree checkpoint tools + session scanner +
/// resume argv. Checkpoint roundtrip com git real está em
/// WorkspaceCheckpointServiceTests.
/// </summary>
public class P3ToolsAndScannerTests : IDisposable
{
    private readonly string _root = Path.Join(
        Path.GetTempPath(), $"p3-{Guid.NewGuid():N}");

    private static ChatToolContext Ctx(string? defaultCli = "codex") =>
        new(
            WorkspacePath: Path.GetTempPath(),
            ProviderId: Guid.NewGuid(),
            ProviderBaseUrl: "http://provider.test",
            ProviderApiKey: "sk",
            ImageModel: "",
            SearchBackend: "none",
            SearchUrl: "",
            SearchApiKey: "",
            ConversationId: "conv-1",
            Model: "m1",
            DelegationDepth: 0,
            DefaultAgentCli: defaultCli);

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static IServiceScopeFactory Scope(Action<ServiceCollection> register)
    {
        var collection = new ServiceCollection();
        register(collection);
        return collection.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- agent_decide ----

    [Fact]
    public async Task Dado_Pergunta_Quando_AgentDecide_Entao_PostaDecision()
    {
        var service = Substitute.For<IDelegationService>();
        service.PostDecisionAsync("conv-1", "codex", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MailboxMessageDto(
                "msg-1", "conv-1", "codex", "@all", AgentMailboxKinds.Decision,
                (string)ci[2], DateTime.UtcNow, null));
        var tool = new AgentDecideTool(Scope(c => c.AddSingleton(service)));

        var result = await tool.ExecuteAsync(
            Args("""{"question":"use sqlite or postgres?"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("msg-1");
        await service.Received(1).PostDecisionAsync(
            "conv-1", "codex", Arg.Is<string>(q => q.Contains("sqlite")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SemPergunta_Quando_AgentDecide_Entao_Recusa()
    {
        var tool = new AgentDecideTool(Scope(c => c.AddSingleton(Substitute.For<IDelegationService>())));

        var result = await tool.ExecuteAsync(Args("{}"), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- worktree checkpoint tools ----

    [Fact]
    public async Task Dado_RunComWorktree_Quando_Checkpoint_Entao_Cria()
    {
        var checkpoints = Substitute.For<IWorkspaceCheckpointService>();
        checkpoints.CreateCheckpointAsync("run-1", "lbl", Arg.Any<CancellationToken>())
            .Returns(new WorktreeCheckpointDto("abc123", "lbl", DateTimeOffset.UtcNow));
        var tool = new WorktreeCheckpointTool(Scope(c => c.AddSingleton(checkpoints)));

        var result = await tool.ExecuteAsync(
            Args("""{"run_id":"run-1","label":"lbl"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("abc123");
    }

    [Fact]
    public async Task Dado_RunSemWorktree_Quando_Checkpoint_Entao_Recusa()
    {
        var checkpoints = Substitute.For<IWorkspaceCheckpointService>();
        checkpoints.CreateCheckpointAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<WorktreeCheckpointDto>(_ =>
                throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "no worktree for run 'x'"));
        var tool = new WorktreeCheckpointTool(Scope(c => c.AddSingleton(checkpoints)));

        var result = await tool.ExecuteAsync(Args("""{"run_id":"x"}"""), Ctx(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("no worktree");
    }

    [Fact]
    public async Task Dado_Checkpoints_Quando_Lista_Entao_Retorna()
    {
        var checkpoints = Substitute.For<IWorkspaceCheckpointService>();
        checkpoints.ListCheckpointsAsync("run-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new WorktreeCheckpointDto("aaa", "c1", DateTimeOffset.UtcNow)]);
        var tool = new WorktreeCheckpointsTool(Scope(c => c.AddSingleton(checkpoints)));

        var result = await tool.ExecuteAsync(Args("""{"run_id":"run-1"}"""), Ctx(), CancellationToken.None);

        result.Json.ShouldContain("aaa");
    }

    // ---- session scanner ----

    [Fact]
    public async Task Dado_HomeSemTranscripts_Quando_Scan_Entao_Vazio()
    {
        var scanner = new AgentSessionScanner(_root);

        var sessions = await scanner.ScanAsync();

        sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_TranscriptsClaudeCodex_Quando_Scan_Entao_ListaComResume()
    {
        var claudeDir = Path.Join(_root, ".claude", "projects", "-home-user-repos-proj");
        var codexDir = Path.Join(_root, ".codex", "sessions", "2026", "10", "04");
        Directory.CreateDirectory(claudeDir);
        Directory.CreateDirectory(codexDir);
        var uuid = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Join(claudeDir, "sess-abc.jsonl"), "{}");
        File.WriteAllText(Path.Join(codexDir, $"rollout-2026-10-04T10-00-00-{uuid}.jsonl"), "{}");

        var scanner = new AgentSessionScanner(_root);
        var sessions = await scanner.ScanAsync();

        sessions.Count.ShouldBe(2);
        var claude = sessions.Single(s => s.Cli == "claude");
        claude.ResumeCommand.ShouldBe("claude --resume sess-abc");
        claude.WorkingDirectory.ShouldBe("/home/user/repos/proj");
        var codex = sessions.Single(s => s.Cli == "codex");
        codex.SessionId.ShouldBe(uuid);
        codex.ResumeCommand.ShouldBe($"codex resume {uuid}");
    }

    [Fact]
    public async Task Dado_FiltroCli_Quando_Scan_Entao_SoAqueleCli()
    {
        var claudeDir = Path.Join(_root, ".claude", "projects", "slug");
        var codexDir = Path.Join(_root, ".codex", "sessions");
        Directory.CreateDirectory(claudeDir);
        Directory.CreateDirectory(codexDir);
        File.WriteAllText(Path.Join(claudeDir, "a.jsonl"), "{}");
        File.WriteAllText(Path.Join(codexDir, "rollout-b.jsonl"), "{}");

        var scanner = new AgentSessionScanner(_root);
        var sessions = await scanner.ScanAsync(cli: "codex");

        sessions.ShouldAllBe(s => s.Cli == "codex");
    }

    [Fact]
    public void Dado_SpecsComResume_Quando_BuildResumeArgs_Entao_ArgvPorCli()
    {
        AgentCliMap.GetSpec(AgentCliKind.Claude)!.BuildResumeArgs("id1")
            .ShouldBe(["--resume", "id1"]);
        AgentCliMap.GetSpec(AgentCliKind.Codex)!.BuildResumeArgs("id1")
            .ShouldBe(["resume", "id1"]);
        AgentCliMap.GetSpec(AgentCliKind.Devin)!.BuildResumeArgs("id1")
            .ShouldBe(["--resume", "id1"]);
    }
}
