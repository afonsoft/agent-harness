using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context P1: .worktreeinclude parse +
/// negation, auto-detect conventions, copy/symlink application, traversal
/// rejection, never-overwrite.
/// </summary>
public class SharedPathProvisionerTests : IDisposable
{
    private readonly string _repo = Path.Combine(
        Path.GetTempPath(), $"repo-{Guid.NewGuid():N}");
    private readonly string _worktree = Path.Combine(
        Path.GetTempPath(), $"wt-{Guid.NewGuid():N}");
    private readonly FakeRunner _runner = new();

    public SharedPathProvisionerTests()
    {
        Directory.CreateDirectory(_repo);
        Directory.CreateDirectory(_worktree);
    }

    public void Dispose()
    {
        Directory.Delete(_repo, recursive: true);
        Directory.Delete(_worktree, recursive: true);
    }

    private SharedPathProvisioner Sut() =>
        new(_runner, NullLogger<SharedPathProvisioner>.Instance);

    private string RepoFile(string rel, string content = "x")
    {
        var p = Path.Join(_repo, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    // ---------- .worktreeinclude parse ----------

    [Fact]
    public async Task Dado_IncludeFile_Quando_Provision_Entao_ListaPathsCopy()
    {
        RepoFile(".worktreeinclude",
            "# comment\n\nnode_modules\nassets/cache\n!secrets\n");
        RepoFile("node_modules/pkg/index.js");
        RepoFile("assets/cache/x.bin");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        var include = result.Where(
            e => e.Source == WorktreeSharedSources.Include).ToList();
        include.Count.ShouldBe(2);
        include.ShouldAllBe(e => e.Mode == WorktreeSharedModes.Copy);
        result.ShouldNotContain(e => e.Path == "secrets");
    }

    [Fact]
    public async Task Dado_Negacao_Quando_Provision_Entao_ExcluiDoAuto()
    {
        RepoFile(".worktreeinclude", "!node_modules\n");
        Directory.CreateDirectory(Path.Join(_repo, "node_modules"));

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        result.ShouldNotContain(e => e.Path == "node_modules");
    }

    // ---------- auto-detect ----------

    [Fact]
    public async Task Dado_NodeModulesExiste_Quando_Provision_Entao_AutoCopy()
    {
        RepoFile("node_modules/pkg/index.js", "module.exports=1");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        var entry = result.Single(e => e.Path == "node_modules");
        entry.Source.ShouldBe(WorktreeSharedSources.Auto);
        entry.Mode.ShouldBe(WorktreeSharedModes.Copy);
        entry.Applied.ShouldBeTrue();
        File.Exists(Path.Join(_worktree, "node_modules/pkg/index.js"))
            .ShouldBeTrue();
        _runner.Calls.ShouldContain(
            a => a.Any(x => x.Contains("node_modules")));
    }

    [Fact]
    public async Task Dado_EnvExiste_Quando_Provision_Entao_SkipPorDefault()
    {
        RepoFile(".env", "SECRET=1");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        var env = result.Single(e => e.Path == ".env");
        env.Mode.ShouldBe(WorktreeSharedModes.Skip);
        env.Applied.ShouldBeFalse();
        File.Exists(Path.Join(_worktree, ".env")).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_IncludeDeclaraAutoPath_Quando_Provision_Entao_SemDuplicata()
    {
        RepoFile(".worktreeinclude", "node_modules\n");
        RepoFile("node_modules/pkg/index.js");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        result.Count(e => e.Path == "node_modules").ShouldBe(1);
        result.Single(e => e.Path == "node_modules").Source
            .ShouldBe(WorktreeSharedSources.Include);
    }

    // ---------- traversal ----------

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../../b")]
    [InlineData("/etc/passwd")]
    public async Task Dado_Traversal_Quando_Provision_Entao_InvalidSharedPath(
        string bad)
    {
        RepoFile(".worktreeinclude", bad);

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        var entry = result.Single(e => e.Path == bad.Trim('/'));
        entry.Applied.ShouldBeFalse();
        entry.Note.ShouldBe("invalid_shared_path");
    }

    // ---------- apply modes ----------

    [Fact]
    public async Task Dado_TargetExiste_Quando_Copy_Entao_NuncaSobrescreve()
    {
        RepoFile(".worktreeinclude", "node_modules\n");
        RepoFile("node_modules/pkg/index.js", "old");
        var existing = Path.Join(_worktree, "node_modules");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Join(existing, "sentinel"), "keep");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        result.Single(e => e.Path == "node_modules").Note.ShouldBe("exists");
        File.ReadAllText(Path.Join(existing, "sentinel")).ShouldBe("keep");
    }

    [Fact]
    public async Task Dado_SourceAusente_Quando_Provision_Entao_Missing()
    {
        RepoFile(".worktreeinclude", "no_such_dir\n");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        result.Single(e => e.Path == "no_such_dir").Note.ShouldBe("missing");
    }

    [Fact]
    public async Task Dado_Copia_Quando_Provision_Entao_CpComReflink()
    {
        RepoFile(".worktreeinclude", "bin\n");
        RepoFile("bin/tool.sh");

        var result = await Sut().ProvisionAsync(_repo, _worktree);

        result.Single(e => e.Path == "bin").Applied.ShouldBeTrue();
        var call = _runner.Calls.Single(c => c.Contains("-a"));
        call.ShouldContain("--reflink=auto");
        call.ShouldContain("--");
    }

    [Fact]
    public async Task Dado_Symlink_Quando_Provision_Entao_LinkRelativo()
    {
        RepoFile("assets/data.bin", "blob");

        var entry = await Sut().ApplyAsync(
            _repo, _worktree,
            new WorktreeSharedResolution(
                "assets/data.bin", WorktreeSharedModes.Symlink,
                WorktreeSharedSources.Include, Invalid: false),
            CancellationToken.None);

        entry.Applied.ShouldBeTrue();
        var target = Path.Join(_worktree, "assets/data.bin");
        new FileInfo(target).LinkTarget.ShouldNotBeNull();
        File.ReadAllText(target).ShouldBe("blob");
        _runner.Calls.ShouldBeEmpty(); // symlink never shells out to cp
    }

    [Fact]
    public async Task Dado_SymlinkTargetExiste_Quando_Provision_Entao_NuncaSobrescreve()
    {
        RepoFile("assets/data.bin", "blob");
        var target = Path.Join(_worktree, "assets/data.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "keep");

        var entry = await Sut().ApplyAsync(
            _repo, _worktree,
            new WorktreeSharedResolution(
                "assets/data.bin", WorktreeSharedModes.Symlink,
                WorktreeSharedSources.Include, Invalid: false),
            CancellationToken.None);

        entry.Applied.ShouldBeFalse();
        entry.Note.ShouldBe("exists");
        File.ReadAllText(target).ShouldBe("keep");
    }

    // ---------- preamble (service side) ----------

    [Fact]
    public void Dado_TaskComSharedPaths_Quando_Preamble_Entao_Inclui()
    {
        var task = Taskboard.Domain.Entities.Delegation.DelegationTask.Create(
            "do it", "claude", "scope", "/ws",
            useWorktree: true, repositoryPath: "/r");
        task.AttachWorktree("run-9", ["node_modules", "bin"]);

        task.SharedPathsApplied.ShouldBe(["node_modules", "bin"]);
        task.WorktreeRunId.ShouldBe("run-9");
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            string executable, string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            // Mirror the real cp: materialize dst = last arg as a copy of src.
            var src = arguments[^2];
            var dst = arguments[^1];
            if (Directory.Exists(src))
            {
                CopyDir(src, dst);
            }
            else if (File.Exists(src))
            {
                File.Copy(src, dst);
            }

            return Task.FromResult(new ProcessRunResult(0, "", "", false));
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                File.Copy(f, Path.Join(dst, Path.GetFileName(f)), overwrite: true);
            }

            foreach (var d in Directory.GetDirectories(src))
            {
                CopyDir(d, Path.Join(dst, Path.GetFileName(d)));
            }
        }
    }
}
