using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class GitWorktreeManagerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"wtm-{Guid.NewGuid():N}");
    private readonly string _repoPath;
    private readonly string _worktreeRoot;
    private readonly GitCommandRunner _git = new();
    private readonly InMemoryWorktreeSessionRepository _sessions = new();
    private readonly GitWorktreeManager _sut;

    public GitWorktreeManagerTests()
    {
        _repoPath = Path.Combine(_tempRoot, "repo");
        _worktreeRoot = Path.Combine(_tempRoot, "worktrees");
        Directory.CreateDirectory(_repoPath);
        InitRepo(_repoPath);
        _sut = new GitWorktreeManager(_git, _sessions, _worktreeRoot, NullLogger<GitWorktreeManager>.Instance);
    }

    // Covers RF-001 / AC-01: worktree criado com branch dedicada
    [Fact]
    public async Task Dado_RepoValido_Quando_CreateWorktree_Entao_DiretorioEBranchCriados()
    {
        var dto = await _sut.CreateWorktreeAsync("run_01", _repoPath, "main", "fix-login-error");

        dto.Status.ShouldBe("Active");
        dto.Path.ShouldBe(WorktreePaths.SessionDir(_worktreeRoot, "run_01"));
        dto.Branch.ShouldBe("feature/agent-run_01-fix-login-error");
        Directory.Exists(dto.Path).ShouldBeTrue();
        File.Exists(Path.Combine(dto.Path, "README.md")).ShouldBeTrue();

        var head = await _git.RunAsync(dto.Path, ["rev-parse", "--abbrev-ref", "HEAD"]);
        head.StandardOutput.Trim().ShouldBe(dto.Branch);
    }

    // Guardrail: runId nunca escapa de ~/.taskboard/worktrees
    [Fact]
    public async Task Dado_RunIdComTraversal_Quando_CreateWorktree_Entao_PathConfinadoNoRoot()
    {
        var dto = await _sut.CreateWorktreeAsync("../evil/../escape", _repoPath, "main", "x");

        WorktreePaths.IsUnder(_worktreeRoot, dto.Path).ShouldBeTrue();
        dto.Path.ShouldBe(WorktreePaths.SessionDir(_worktreeRoot, "../evil/../escape"));
    }

    // Covers RF-002 / AC-02: diff retorna arquivos + patch
    [Fact]
    public async Task Dado_WorktreeComAlteracoes_Quando_GetDiff_Entao_ArquivosEPatch()
    {
        var dto = await _sut.CreateWorktreeAsync("run_02", _repoPath, "main", "change");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "README.md"), "changed content\n");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "novo.cs"), "class Novo {}\n");

        var diff = await _sut.GetDiffAsync("run_02");

        diff.FilesChanged.ShouldBeGreaterThanOrEqualTo(2);
        diff.Files.ShouldContain(f => f.Path == "README.md");
        diff.Files.ShouldContain(f => f.Path == "novo.cs" && f.Status == "Untracked");
        diff.Patch.ShouldContain("changed content");
    }

    // Covers RF-003: commit estruturado retorna sha e persiste na sessão
    [Fact]
    public async Task Dado_AlteracoesNoWorktree_Quando_Commit_Entao_ShaRegistrado()
    {
        var dto = await _sut.CreateWorktreeAsync("run_03", _repoPath, "main", "commit-test");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "feat.cs"), "class Feat {}\n");

        var sha = await _sut.CommitAsync("run_03", "feat(agent): run_03 automated task", "Harness <harness@local>");

        sha.ShouldNotBeNullOrWhiteSpace();
        var session = _sessions.Items.Single(s => s.RunId == "run_03");
        session.CommitSha.ShouldBe(sha);
        session.Status.ShouldBe("Active");

        var log = await _git.RunAsync(dto.Path, ["log", "-1", "--format=%s"]);
        log.StandardOutput.ShouldContain("automated task");
    }

    // Covers RF-004 / AC-04: remove limpa diretório e worktree list
    [Fact]
    public async Task Dado_WorktreeAtivo_Quando_Remove_Entao_DiretorioRemovidoEStatusRemoved()
    {
        var dto = await _sut.CreateWorktreeAsync("run_04", _repoPath, "main", "cleanup");

        await _sut.RemoveWorktreeAsync("run_04");

        Directory.Exists(dto.Path).ShouldBeFalse();
        var list = await _git.RunAsync(_repoPath, ["worktree", "list", "--porcelain"]);
        list.StandardOutput.ShouldNotContain(dto.Path);
        _sessions.Items.Single(s => s.RunId == "run_04").Status.ShouldBe("Removed");
    }

    // Edge case: mesmo runId (retry) → reutilização idempotente
    [Fact]
    public async Task Dado_MesmoRunId_Quando_CreateDuasVezes_Entao_ReutilizaSessao()
    {
        var first = await _sut.CreateWorktreeAsync("run_05", _repoPath, "main", "retry");
        var second = await _sut.CreateWorktreeAsync("run_05", _repoPath, "main", "retry");

        second.WorktreeId.ShouldBe(first.WorktreeId);
        second.Branch.ShouldBe(first.Branch);
        _sessions.Items.Count(s => s.RunId == "run_05").ShouldBe(1);
    }

    // AC-03: runs simultâneos no mesmo repo não interferem
    [Fact]
    public async Task Dado_DoisRuns_Quando_Paralelo_Entao_WorktreesIndependem()
    {
        var a = await _sut.CreateWorktreeAsync("run_a", _repoPath, "main", "task-a");
        var b = await _sut.CreateWorktreeAsync("run_b", _repoPath, "main", "task-b");

        await File.WriteAllTextAsync(Path.Combine(a.Path, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(b.Path, "b.txt"), "b");

        File.Exists(Path.Combine(a.Path, "b.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(b.Path, "a.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(_repoPath, "a.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(_repoPath, "b.txt")).ShouldBeFalse();
    }

    // Guardrail: slug com metacaracteres é sanitizado no nome da branch
    [Fact]
    public async Task Dado_SlugComMetacaracteres_Quando_CreateWorktree_Entao_BranchSanitizada()
    {
        var dto = await _sut.CreateWorktreeAsync("run_06", _repoPath, "main", "fix; rm -rf / $(whoami)");

        dto.Branch.ShouldStartWith("feature/agent-run_06-");
        dto.Branch.ShouldNotContain(";");
        dto.Branch.ShouldNotContain("$");
        dto.Branch.ShouldNotContain(" ");
    }

    // Edge case: base com dirty working tree → worktree nasce limpo do HEAD
    [Fact]
    public async Task Dado_BaseComDirtyTree_Quando_CreateWorktree_Entao_WorktreeLimpo()
    {
        await File.WriteAllTextAsync(Path.Combine(_repoPath, "dirty.txt"), "uncommitted");

        var dto = await _sut.CreateWorktreeAsync("run_07", _repoPath, "main", "clean");

        File.Exists(Path.Combine(dto.Path, "dirty.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SessaoInexistente_Quando_GetDiff_Entao_DomainException()
    {
        await Should.ThrowAsync<DomainException>(() => _sut.GetDiffAsync("run_inexistente"));
    }

    private void InitRepo(string path)
    {
        var git = new GitCommandRunner();
        git.RunAsync(path, ["init", "-b", "main"]).GetAwaiter().GetResult().ExitCode.ShouldBe(0);
        git.RunAsync(path, ["config", "user.email", "test@local"]).GetAwaiter().GetResult();
        git.RunAsync(path, ["config", "user.name", "Test"]).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(path, "README.md"), "# repo\n");
        git.RunAsync(path, ["add", "-A"]).GetAwaiter().GetResult();
        git.RunAsync(path, ["commit", "-m", "init"]).GetAwaiter().GetResult().ExitCode.ShouldBe(0);
    }

    public void Dispose()
    {
        try
        {
            // Remove worktrees antes de deletar o temp root (git guarda metadados na base).
            foreach (var session in _sessions.Items.ToList())
            {
                if (Directory.Exists(session.Path))
                {
                    _git.RunAsync(session.RepositoryPath, ["worktree", "remove", "--force", session.Path])
                        .GetAwaiter().GetResult();
                }
            }

            _git.RunAsync(_repoPath, ["worktree", "prune"]).GetAwaiter().GetResult();
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private sealed class InMemoryWorktreeSessionRepository : IWorktreeSessionRepository
    {
        public List<WorktreeSessionDto> Items { get; } = new();

        public Task<WorktreeSessionDto> CreateAsync(
            string runId,
            string repositoryPath,
            string baseBranch,
            string path,
            string branch,
            bool retainOnFailure,
            CancellationToken cancellationToken = default)
        {
            var dto = new WorktreeSessionDto(
                Guid.NewGuid().ToString("N"),
                runId,
                path,
                branch,
                "Active",
                repositoryPath,
                baseBranch,
                null,
                retainOnFailure,
                DateTime.UtcNow,
                DateTime.UtcNow,
                1);
            Items.Add(dto);
            return Task.FromResult(dto);
        }

        public Task<WorktreeSessionDto?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.RunId == runId));

        public Task<IReadOnlyList<WorktreeSessionDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorktreeSessionDto>>(Items.ToList());

        public Task<WorktreeSessionDto> RecordCommitAsync(string runId, string commitSha, CancellationToken cancellationToken = default)
        {
            var dto = Items.Single(s => s.RunId == runId);
            var updated = dto with { CommitSha = commitSha };
            Items[Items.IndexOf(dto)] = updated;
            return Task.FromResult(updated);
        }

        public Task<WorktreeSessionDto> SetStatusAsync(string runId, WorktreeStatus status, CancellationToken cancellationToken = default)
        {
            var dto = Items.Single(s => s.RunId == runId);
            var updated = dto with { Status = status.ToString() };
            Items[Items.IndexOf(dto)] = updated;
            return Task.FromResult(updated);
        }
    }
}
