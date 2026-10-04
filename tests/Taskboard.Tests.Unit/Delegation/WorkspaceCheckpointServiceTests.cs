using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261006 RF-004: checkpoints com git real em um worktree de teste —
/// create/list/restore roundtrip + casos de erro.
/// </summary>
public class WorkspaceCheckpointServiceTests : IDisposable
{
    private readonly string _repo = Path.Join(
        Path.GetTempPath(), $"cpk-{Guid.NewGuid():N}");
    private readonly IWorkspaceIsolationService _worktrees = Substitute.For<IWorkspaceIsolationService>();
    private readonly WorkspaceCheckpointService _service;

    public WorkspaceCheckpointServiceTests()
    {
        Directory.CreateDirectory(_repo);
        Git("init").Wait();
        Git("config", "user.email", "t@t").Wait();
        Git("config", "user.name", "t").Wait();
        File.WriteAllText(Path.Join(_repo, "a.txt"), "v1");
        Git("add", "-A").Wait();
        Git("commit", "-m", "init").Wait();

        _worktrees.GetAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt", "run-1", _repo, "b", "active",
                _repo, "main", null, false, DateTime.UtcNow, DateTime.UtcNow, 0));
        _service = new WorkspaceCheckpointService(_worktrees, new GitCommandRunner());
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private Task<GitCommandResult> Git(params string[] args) => new GitCommandRunner()
        .RunAsync(_repo, args, TimeSpan.FromSeconds(15), CancellationToken.None);

    [Fact]
    public async Task Dado_Mudancas_Quando_CriaCheckpoint_Entao_CommitaELista()
    {
        File.WriteAllText(Path.Join(_repo, "b.txt"), "new");

        var checkpoint = await _service.CreateCheckpointAsync("run-1", "before refactor");

        checkpoint.Label.ShouldBe("before refactor");
        checkpoint.Sha.ShouldNotBeNullOrEmpty();

        var list = await _service.ListCheckpointsAsync("run-1");
        list.ShouldContain(c => c.Sha == checkpoint.Sha && c.Label == "before refactor");
    }

    [Fact]
    public async Task Dado_WorktreeLimpa_Quando_CriaCheckpoint_Entao_UsaHead()
    {
        var checkpoint = await _service.CreateCheckpointAsync("run-1", "clean");

        var head = await Git("rev-parse", "HEAD");
        checkpoint.Sha.ShouldBe(head.StandardOutput.Trim());
    }

    [Fact]
    public async Task Dado_Checkpoint_Quando_Restaura_Entao_VoltaEstado()
    {
        File.WriteAllText(Path.Join(_repo, "a.txt"), "v1");
        var checkpoint = await _service.CreateCheckpointAsync("run-1", "v1");
        File.WriteAllText(Path.Join(_repo, "a.txt"), "v2-broken");

        await _service.RestoreCheckpointAsync("run-1", checkpoint.Sha);

        File.ReadAllText(Path.Join(_repo, "a.txt")).ShouldBe("v1");
    }

    [Fact]
    public async Task Dado_ShaDesconhecido_Quando_Restaura_Entao_Recusa()
    {
        await _service.CreateCheckpointAsync("run-1", "c1");

        await Should.ThrowAsync<DomainException>(() =>
            _service.RestoreCheckpointAsync("run-1", new string('f', 40)));
    }

    [Fact]
    public async Task Dado_RunSemWorktree_Quando_CriaCheckpoint_Entao_Recusa()
    {
        _worktrees.GetAsync("ghost", Arg.Any<CancellationToken>())
            .Returns(default(WorktreeSessionDto));

        await Should.ThrowAsync<DomainException>(() =>
            _service.CreateCheckpointAsync("ghost"));
    }
}
