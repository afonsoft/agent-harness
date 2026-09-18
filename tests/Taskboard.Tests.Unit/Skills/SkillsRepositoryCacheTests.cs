using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Integrations.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Skills;

public class SkillsRepositoryCacheTests : IDisposable
{
    private readonly string _root;
    private readonly string _cache;

    public SkillsRepositoryCacheTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-cache-{Guid.NewGuid():N}");
        _cache = Path.Join(_root, "skills-cache");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Restore permissions on anything the tests locked down so cleanup works.
        RestoreModes(_root);
        Directory.Delete(_root, recursive: true);
    }

    private static void RestoreModes(string directory)
    {
        TrySetMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                TrySetMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            foreach (var dir in Directory.EnumerateDirectories(directory))
            {
                RestoreModes(dir);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TrySetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Dado_CacheAusente_Quando_EnsureAccessible_Entao_Healthy()
    {
        // Covers RF-001 edge: nothing to probe when the cache does not exist
        var result = SkillsRepository.EnsureAccessible(_cache, NullLogger.Instance);

        result.ShouldBe(CachePrepareResult.Healthy);
    }

    [Fact]
    public void Dado_CacheSaudavel_Quando_EnsureAccessible_Entao_HealthySemRename()
    {
        // Covers RF-006: a usable cache is left untouched — no quarantine dirs
        Directory.CreateDirectory(Path.Join(_cache, "skills", "alpha"));
        File.WriteAllText(Path.Join(_cache, "skills", "alpha", "SKILL.md"), "x");

        var result = SkillsRepository.EnsureAccessible(_cache, NullLogger.Instance);

        result.ShouldBe(CachePrepareResult.Healthy);
        Directory.Exists(_cache).ShouldBeTrue();
        Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ArquivoReadOnlyNoCache_Quando_EnsureAccessible_Entao_Healthy()
    {
        // Regression: git marks .git/objects/pack files read-only (444) by
        // design — a legitimately read-only file in a writable tree is
        // healthy; git replaces files via unlink+create in the parent dir
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var packDir = Path.Join(_cache, ".git", "objects", "pack");
        Directory.CreateDirectory(packDir);
        var pack = Path.Join(packDir, "pack-abc123.pack");
        File.WriteAllText(pack, "x");
        File.SetUnixFileMode(pack,
            UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var result = SkillsRepository.EnsureAccessible(_cache, NullLogger.Instance);

        result.ShouldBe(CachePrepareResult.Healthy);
        Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_CacheSemPermissao_Quando_EnsureAccessible_Entao_RecoveredERenomeia()
    {
        // Covers RF-001 + AC1: an unreadable cache is moved aside, never deleted
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_cache);
        File.WriteAllText(Path.Join(_cache, "install.sh"), "#!/bin/sh\n");
        File.SetUnixFileMode(_cache, 0);

        var result = SkillsRepository.EnsureAccessible(_cache, NullLogger.Instance);

        result.ShouldBe(CachePrepareResult.Recovered);
        Directory.Exists(_cache).ShouldBeFalse();
        var stale = Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").Single();

        TrySetMode(stale, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Exists(Path.Join(stale, "install.sh")).ShouldBeTrue();
    }

    [Fact]
    public void Dado_ArquivoInacessivelNoCache_Quando_EnsureAccessible_Entao_Recovered()
    {
        // Covers RF-001 deep probe: writable top dir but a foreign-owned file
        // inside (simulated by mode 000) still triggers recovery — git checkout
        // would fail on it mid-run otherwise
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var subdir = Path.Join(_cache, "scripts");
        Directory.CreateDirectory(subdir);
        var locked = Path.Join(subdir, "audit.py");
        File.WriteAllText(locked, "x");
        File.SetUnixFileMode(locked, 0);

        var result = SkillsRepository.EnsureAccessible(_cache, NullLogger.Instance);

        result.ShouldBe(CachePrepareResult.Recovered);
        Directory.Exists(_cache).ShouldBeFalse();
        var stale = Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").Single();

        TrySetMode(Path.Join(stale, "scripts", "audit.py"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Dado_PaiSemEscrita_Quando_EnsureAccessible_Entao_ErroAcionavel()
    {
        // Covers AC2: rename impossible (parent not writable) → explicit,
        // actionable error naming the path and the manual fix
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Join(_root, "locked-parent");
        var cache = Path.Join(parent, "skills-cache");
        Directory.CreateDirectory(cache);
        File.SetUnixFileMode(cache, 0);
        File.SetUnixFileMode(parent,
            UnixFileMode.UserRead | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var ex = Should.Throw<InvalidOperationException>(
            () => SkillsRepository.EnsureAccessible(cache, NullLogger.Instance));

        ex.Message.ShouldContain(cache);
        ex.Message.ShouldContain("chown");

        TrySetMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    [Fact]
    public async Task Dado_ScriptSemBitExec_Quando_EnsureCache_Entao_ChmodMaisX()
    {
        // Covers RF-002 + AC4: cloned .sh files without the execute bit get
        // chmod +x after the clone — idempotent on the next run
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await SkillsRepository.EnsureCacheAsync(
            _cache,
            _root, // rooted path → treated as a local clone URL
            token: null,
            git: FakeGit,
            NullLogger.Instance,
            CancellationToken.None);

        result.ShouldBe(CachePrepareResult.Healthy);
        var mode = File.GetUnixFileMode(Path.Join(_cache, "install.sh"));
        mode.HasFlag(UnixFileMode.UserExecute).ShouldBeTrue();
        mode.HasFlag(UnixFileMode.GroupExecute).ShouldBeTrue();
        mode.HasFlag(UnixFileMode.OtherExecute).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_CacheInacessivel_Quando_EnsureCache_Entao_RecoveredECloneLimpo()
    {
        // Covers AC1 end-to-end at the repository layer: inaccessible cache is
        // renamed aside and a fresh clone takes its place
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_cache);
        File.SetUnixFileMode(_cache, 0);

        var result = await SkillsRepository.EnsureCacheAsync(
            _cache, _root, null, FakeGit, NullLogger.Instance, CancellationToken.None);

        result.ShouldBe(CachePrepareResult.Recovered);
        Directory.Exists(Path.Join(_cache, ".git")).ShouldBeTrue();
        Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").ShouldNotBeEmpty();

        var stale = Directory.EnumerateDirectories(_root, "skills-cache.inaccessible-*").Single();
        TrySetMode(stale, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static Task<GitResult> FakeGit(
        string workingDirectory,
        string? authToken,
        CancellationToken cancellationToken,
        params string[] args)
    {
        if (args.Length > 0 && args[0] == "clone")
        {
            var target = args[^1];
            Directory.CreateDirectory(Path.Join(target, ".git"));
            var script = Path.Join(target, "install.sh");
            File.WriteAllText(script, "#!/bin/sh\nexit 0\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(script,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
        }

        return Task.FromResult(new GitResult(0, string.Empty, string.Empty));
    }
}
