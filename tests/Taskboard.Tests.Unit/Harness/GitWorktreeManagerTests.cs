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

    // Edge case: sessão persistida mas diretório removido (crash/manual rm) →
    // recria o worktree reutilizando a mesma linha (RunId é unique).
    [Fact]
    public async Task Dado_SessaoSemDiretorio_Quando_CreateWorktree_Entao_RecriaSemDuplicarSessao()
    {
        var first = await _sut.CreateWorktreeAsync("run_08", _repoPath, "main", "lost");
        Directory.Delete(first.Path, recursive: true);
        await _git.RunAsync(_repoPath, ["worktree", "prune"]);

        var second = await _sut.CreateWorktreeAsync("run_08", _repoPath, "main", "lost");

        Directory.Exists(second.Path).ShouldBeTrue();
        second.WorktreeId.ShouldBe(first.WorktreeId);
        _sessions.Items.Count(s => s.RunId == "run_08").ShouldBe(1);
    }

    // Edge case: base inexistente no clone (ex.: "main" num repo cujo default é
    // "master") → resolve para o default do clone em vez de falhar o attach.
    [Fact]
    public async Task Dado_BaseInexistente_Quando_CreateWorktree_Entao_FallbackParaDefaultDoClone()
    {
        var masterRepo = Path.Combine(_tempRoot, "repo-master");
        Directory.CreateDirectory(masterRepo);
        InitRepo(masterRepo, "master");

        var dto = await _sut.CreateWorktreeAsync("run_09", masterRepo, "main", "fallback");

        Directory.Exists(dto.Path).ShouldBeTrue();
        // Sem remote no teste → origin/HEAD não existe → cai para HEAD.
        dto.BaseBranch.ShouldBe("HEAD");
        var head = await _git.RunAsync(dto.Path, ["rev-parse", "--abbrev-ref", "HEAD"]);
        head.StandardOutput.Trim().ShouldBe(dto.Branch);
    }

    [Fact]
    public async Task Dado_SessaoInexistente_Quando_GetDiff_Entao_DomainException()
    {
        await Should.ThrowAsync<DomainException>(() => _sut.GetDiffAsync("run_inexistente"));
    }

    // RF-003: explorer — lista diretórios sem .git, paths relativos ao worktree
    [Fact]
    public async Task Dado_WorktreeComArquivos_Quando_ListFiles_Entao_ListaSemGitERelativo()
    {
        var dto = await _sut.CreateWorktreeAsync("run_e1", _repoPath, "main", "explorer");
        Directory.CreateDirectory(Path.Combine(dto.Path, "src"));
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "src", "App.cs"), "class App {}\n");

        var root = await _sut.ListFilesAsync("run_e1", null);

        root.ShouldNotBeNull();
        root.Entries.ShouldContain(e => e.Name == "src" && e.Directory && e.Path == "src");
        root.Entries.ShouldContain(e => e.Name == "README.md" && !e.Directory && e.Path == "README.md");
        root.Entries.ShouldNotContain(e => e.Name == ".git");
        root.Entries.First().Directory.ShouldBeTrue(); // diretórios primeiro

        var src = await _sut.ListFilesAsync("run_e1", "src");
        src.ShouldNotBeNull();
        src.Path.ShouldBe("src");
        src.Entries.ShouldContain(e => e.Name == "App.cs" && e.Path == "src/App.cs" && e.SizeBytes > 0);
    }

    [Fact]
    public async Task Dado_PathTraversal_Quando_ListFiles_Entao_DomainException()
    {
        await _sut.CreateWorktreeAsync("run_e2", _repoPath, "main", "guard");

        await Should.ThrowAsync<DomainException>(() => _sut.ListFilesAsync("run_e2", "../repo"));
        await Should.ThrowAsync<DomainException>(() => _sut.ListFilesAsync("run_e2", "/etc"));
    }

    [Fact]
    public async Task Dado_DiretorioInexistente_Quando_ListFiles_Entao_Null()
    {
        await _sut.CreateWorktreeAsync("run_e3", _repoPath, "main", "missing");
        (await _sut.ListFilesAsync("run_e3", "nao-existe")).ShouldBeNull();
    }

    // RF-003: leitura confinada — conteúdo, binário, inexistente e traversal
    [Fact]
    public async Task Dado_ArquivoTexto_Quando_ReadFile_Entao_ConteudoCompleto()
    {
        var dto = await _sut.CreateWorktreeAsync("run_e4", _repoPath, "main", "read");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "nota.md"), "# nota\n");

        var file = await _sut.ReadFileAsync("run_e4", "nota.md");

        file.ShouldNotBeNull();
        file.Content.ShouldBe("# nota\n");
        file.Binary.ShouldBeFalse();
        file.Truncated.ShouldBeFalse();
        file.SizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Dado_ArquivoBinario_Quando_ReadFile_Entao_BinarySemConteudo()
    {
        var dto = await _sut.CreateWorktreeAsync("run_e5", _repoPath, "main", "binary");
        await File.WriteAllBytesAsync(Path.Combine(dto.Path, "img.bin"), [0x89, 0x50, 0x00, 0x47]);

        var file = await _sut.ReadFileAsync("run_e5", "img.bin");

        file.ShouldNotBeNull();
        file.Binary.ShouldBeTrue();
        file.Content.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ArquivoGrande_Quando_ReadFile_Entao_Truncated()
    {
        var dto = await _sut.CreateWorktreeAsync("run_e6", _repoPath, "main", "large");
        await File.WriteAllTextAsync(
            Path.Combine(dto.Path, "grande.txt"), new string('x', GitWorktreeManager.MaxContentBytes + 100));

        var file = await _sut.ReadFileAsync("run_e6", "grande.txt");

        file.ShouldNotBeNull();
        file.Truncated.ShouldBeTrue();
        file.Content!.Length.ShouldBe(GitWorktreeManager.MaxContentBytes);
    }

    [Fact]
    public async Task Dado_PathTraversal_Quando_ReadFile_Entao_DomainException()
    {
        await _sut.CreateWorktreeAsync("run_e7", _repoPath, "main", "guard-read");

        await Should.ThrowAsync<DomainException>(() => _sut.ReadFileAsync("run_e7", "../../etc/passwd"));
    }

    [Fact]
    public async Task Dado_ArquivoInexistente_Quando_ReadFile_Entao_Null()
    {
        await _sut.CreateWorktreeAsync("run_e8", _repoPath, "main", "missing-file");
        (await _sut.ReadFileAsync("run_e8", "fantasma.cs")).ShouldBeNull();
    }

    // RF-004: --numstat por arquivo preenche contadores do DTO
    [Fact]
    public async Task Dado_DiffComArquivos_Quando_GetDiff_Entao_ContadoresPorArquivo()
    {
        var dto = await _sut.CreateWorktreeAsync("run_e9", _repoPath, "main", "numstat");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "README.md"), "l1\nl2\nl3\n");
        await File.WriteAllTextAsync(Path.Combine(dto.Path, "novo.cs"), "class Novo {}\n");

        var diff = await _sut.GetDiffAsync("run_e9");

        var readme = diff.Files.Single(f => f.Path == "README.md");
        (readme.Insertions + readme.Deletions).ShouldBeGreaterThan(0);
        diff.Files.Single(f => f.Path == "novo.cs").Insertions.ShouldBe(0); // untracked não entra no diff
        diff.Insertions.ShouldBeGreaterThanOrEqualTo(readme.Insertions);
    }

    private void InitRepo(string path, string branch = "main")
    {
        var git = new GitCommandRunner();
        git.RunAsync(path, ["init", "-b", branch]).GetAwaiter().GetResult().ExitCode.ShouldBe(0);
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
            var existing = Items.FirstOrDefault(s => s.RunId == runId);
            if (existing is not null)
            {
                var reactivated = existing with
                {
                    Path = path,
                    Branch = branch,
                    BaseBranch = baseBranch,
                    Status = "Active",
                    CommitSha = null,
                    RetainOnFailure = retainOnFailure,
                    UpdatedAt = DateTime.UtcNow,
                    Version = existing.Version + 1,
                };
                Items[Items.IndexOf(existing)] = reactivated;
                return Task.FromResult(reactivated);
            }

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
