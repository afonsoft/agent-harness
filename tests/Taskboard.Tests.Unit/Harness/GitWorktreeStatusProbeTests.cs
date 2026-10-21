using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Harness;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-007: porcelain-derived status —
/// empty → clean, entries → dirty, unmerged codes → conflict, failure →
/// unknown. Runs real git against temp repos.
/// </summary>
public class GitWorktreeStatusProbeTests : IDisposable
{
    private readonly string _repo = Path.Combine(
        Path.GetTempPath(), $"probe-{Guid.NewGuid():N}");
    private readonly GitCommandRunner _git = new();

    public GitWorktreeStatusProbeTests()
    {
        Directory.CreateDirectory(_repo);
        _git.RunAsync(_repo, ["init", "-b", "main"]).GetAwaiter().GetResult()
            .ExitCode.ShouldBe(0);
        _git.RunAsync(_repo, ["config", "user.email", "test@local"])
            .GetAwaiter().GetResult();
        _git.RunAsync(_repo, ["config", "user.name", "Test"])
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); }
        catch { /* best-effort */ }
    }

    private GitWorktreeStatusProbe Sut() =>
        new(_git, NullLogger<GitWorktreeStatusProbe>.Instance);

    private async Task CommitFileAsync(string name, string content)
    {
        await File.WriteAllTextAsync(Path.Join(_repo, name), content);
        (await _git.RunAsync(_repo, ["add", "-A"])).ExitCode.ShouldBe(0);
        (await _git.RunAsync(_repo, ["commit", "-m", "c"]))
            .ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_RepoLimpo_Quando_Probe_Entao_Clean()
    {
        await CommitFileAsync("a.txt", "x");

        (await Sut().ProbeAsync(_repo)).ShouldBe(WorkspaceStatus.Clean);
    }

    [Fact]
    public async Task Dado_Modificacao_Quando_Probe_Entao_Dirty()
    {
        await CommitFileAsync("a.txt", "x");
        await File.WriteAllTextAsync(Path.Join(_repo, "a.txt"), "changed");

        (await Sut().ProbeAsync(_repo)).ShouldBe(WorkspaceStatus.Dirty);
    }

    [Fact]
    public async Task Dado_ArquivoNovo_Quando_Probe_Entao_Dirty()
    {
        await CommitFileAsync("a.txt", "x");
        await File.WriteAllTextAsync(Path.Join(_repo, "new.txt"), "n");

        (await Sut().ProbeAsync(_repo)).ShouldBe(WorkspaceStatus.Dirty);
    }

    [Fact]
    public async Task Dado_NaoGit_Quando_Probe_Entao_Unknown()
    {
        var plain = Path.Combine(Path.GetTempPath(), $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(plain);
        try
        {
            (await Sut().ProbeAsync(plain)).ShouldBe(WorkspaceStatus.Unknown);
            (await Sut().ResolveRepoPathAsync(plain)).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }

    [Fact]
    public async Task Dado_Repo_Quando_ResolveRepoPath_Entao_RaizDoCheckout()
    {
        (await Sut().ResolveRepoPathAsync(_repo))
            .ShouldBe(Path.GetFullPath(_repo));
    }

    [Fact]
    public async Task Dado_Worktree_Quando_ResolveRepoPath_Entao_RaizDoRepoPrincipal()
    {
        await CommitFileAsync("a.txt", "x");
        var wt = Path.Combine(Path.GetTempPath(), $"wt-{Guid.NewGuid():N}");
        try
        {
            (await _git.RunAsync(
                    _repo, ["worktree", "add", wt, "-b", "wt-branch"]))
                .ExitCode.ShouldBe(0);

            // The worktree's git-common-dir points back at the main .git —
            // its parent is the real repo root, not the worktree itself.
            (await Sut().ResolveRepoPathAsync(wt))
                .ShouldBe(Path.GetFullPath(_repo));
        }
        finally
        {
            await _git.RunAsync(_repo, ["worktree", "remove", "--force", wt]);
        }
    }
}
