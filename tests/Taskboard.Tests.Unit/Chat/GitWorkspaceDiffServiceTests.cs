using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261011-chat-workspace-panel RF-003/RF-005: StatusAsync e
/// CurrentDiffAsync do <see cref="GitWorkspaceDiffService"/> — status rápido
/// do workspace e diff vs HEAD para a aba Changes do painel.
/// </summary>
public class GitWorkspaceDiffServiceTests : IDisposable
{
    private readonly string _workdir = Path.Combine(Path.GetTempPath(), $"wsdiff-{Guid.NewGuid():N}");

    public GitWorkspaceDiffServiceTests() => Directory.CreateDirectory(_workdir);

    [Fact]
    public async Task Dado_DirForaDeGit_Quando_StatusAsync_Entao_RetornaNull()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(128, "", "not a git repository", TimedOut: false)));

        var status = await sut.StatusAsync(_workdir);

        status.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RepoGit_Quando_StatusAsync_Entao_BranchEDirty()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(0, "true\n", "", TimedOut: false),       // is-inside-work-tree
            new GitCommandResult(0, "main\n", "", TimedOut: false),       // abbrev-ref HEAD
            new GitCommandResult(0, " M a.txt\n?? new.txt\n", "", false))); // status --porcelain

        var status = await sut.StatusAsync(_workdir);

        status.ShouldNotBeNull();
        status.IsGit.ShouldBeTrue();
        status.Branch.ShouldBe("main");
        status.Dirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_RepoLimpo_Quando_StatusAsync_Entao_DirtyFalse()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(0, "true\n", "", TimedOut: false),
            new GitCommandResult(0, "feat/x\n", "", TimedOut: false),
            new GitCommandResult(0, "", "", TimedOut: false)));

        var status = await sut.StatusAsync(_workdir);

        status.ShouldNotBeNull();
        status.Branch.ShouldBe("feat/x");
        status.Dirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_DirInexistente_Quando_StatusAsync_Entao_RetornaNull()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner());
        var missing = Path.Combine(_workdir, "nao-existe");

        var status = await sut.StatusAsync(missing);

        status.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RepoComMudancas_Quando_CurrentDiffAsync_Entao_MergeNumstatEUntracked()
    {
        File.WriteAllLines(Path.Combine(_workdir, "new.txt"), ["l1", "l2", "l3", "l4", "l5"]);
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(0, "true\n", "", TimedOut: false),            // is-inside-work-tree
            new GitCommandResult(0, " M a.txt\n?? new.txt\n", "", false),      // status --porcelain
            new GitCommandResult(0, "3\t1\ta.txt\n", "", false),               // diff --numstat HEAD
            new GitCommandResult(0, "M\ta.txt\n", "", false),                  // diff --name-status HEAD
            new GitCommandResult(0, "diff --git a/a.txt b/a.txt\n@@ -1 +1 @@\n", "", false), // diff HEAD
            new GitCommandResult(0, "new.txt\n", "", TimedOut: false)));       // ls-files --others

        var diff = await sut.CurrentDiffAsync(_workdir);

        diff.ShouldNotBeNull();
        diff.Files.Count.ShouldBe(2);
        diff.Insertions.ShouldBe(8); // 3 tracked + 5 linhas do untracked
        diff.Deletions.ShouldBe(1);
        diff.Files.Single(f => f.Path == "a.txt").Status.ShouldBe("Modified");
        diff.Files.Single(f => f.Path == "new.txt").Status.ShouldBe("Untracked");
        diff.Patch.ShouldContain("diff --git");
    }

    [Fact]
    public async Task Dado_RepoVazioSemHead_Quando_CurrentDiffAsync_Entao_DiffVazioSemFalhar()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(0, "true\n", "", TimedOut: false),
            new GitCommandResult(0, "", "", TimedOut: false),                 // status (limpo)
            new GitCommandResult(128, "", "ambiguous argument 'HEAD'", TimedOut: false),
            new GitCommandResult(128, "", "ambiguous argument 'HEAD'", TimedOut: false),
            new GitCommandResult(128, "", "ambiguous argument 'HEAD'", TimedOut: false)));

        var diff = await sut.CurrentDiffAsync(_workdir);

        diff.ShouldNotBeNull();
        diff.Files.ShouldBeEmpty();
        diff.Insertions.ShouldBe(0);
        diff.Patch.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Dado_DirForaDeGit_Quando_CurrentDiffAsync_Entao_RetornaNull()
    {
        var sut = new GitWorkspaceDiffService(new FakeGitRunner(
            new GitCommandResult(128, "", "not a git repository", TimedOut: false)));

        var diff = await sut.CurrentDiffAsync(_workdir);

        diff.ShouldBeNull();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workdir, recursive: true);
        }
        catch (IOException)
        {
            // Temp residue is harmless on the test host.
        }
    }

    private sealed class FakeGitRunner(params GitCommandResult[] results) : IGitCommandRunner
    {
        private int _calls;

        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> arguments,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (results.Length == 0)
            {
                return Task.FromResult(new GitCommandResult(1, "", "no fakes left", TimedOut: false));
            }

            var index = Math.Min(_calls++, results.Length - 1);
            return Task.FromResult(results[index]);
        }
    }
}
