using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Integrations.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Skills;

public class SkillsSyncServiceTests : IDisposable
{
    private readonly string _root;

    public SkillsSyncServiceTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-sync-{Guid.NewGuid()}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Dado_RepositorioNovo_Quando_Sync_Entao_InstalaSkillsEPreservaExtras()
    {
        // Covers RF-003/RF-005: skills are installed and user-created skills survive
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var customSkill = Path.Join(home, ".claude", "skills", "my-custom-skill");
        Directory.CreateDirectory(customSkill);
        File.WriteAllText(Path.Join(customSkill, "SKILL.md"), "---\nname: my-custom-skill\ndescription: x\n---\n");

        var service = CreateService(repo, home);
        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        status.Repository.ShouldBe(repo);
        var claude = status.Agents.Single();
        claude.AgentType.ShouldBe("Claude");
        claude.Installed.ShouldBe(2);
        claude.Updated.ShouldBe(0);
        claude.Error.ShouldBeNull();

        var skillsDir = Path.Join(home, ".claude", "skills");
        File.Exists(Path.Join(skillsDir, "alpha-skill", "SKILL.md")).ShouldBeTrue();
        File.Exists(Path.Join(skillsDir, "beta-skill", "SKILL.md")).ShouldBeTrue();
        Directory.Exists(customSkill).ShouldBeTrue();
        File.Exists(Path.Join(skillsDir, ".harness-skills.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ManifestLegado_Quando_Sync_Entao_LeManifestAntigo()
    {
        // SPEC-20260922-harness-home-rename RF-007: pre-rename destinations hold
        // .taskboard-skills.json — the sync must still read it as fallback.
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home);
        var skillsDir = Path.Join(home, ".claude", "skills");
        Directory.CreateDirectory(skillsDir);
        File.WriteAllText(Path.Join(skillsDir, ".taskboard-skills.json"),
            """{"skills":{"ghost-skill":{"hash":"abc","syncedAtUtc":"2026-01-01T00:00:00Z","repository":"afonsoft/skills","removedFromSource":false}}}""");

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        var manifest = File.ReadAllText(Path.Join(skillsDir, ".harness-skills.json"));
        manifest.ShouldContain("ghost-skill");
        manifest.ShouldContain("\"removedFromSource\": true");
    }

    [Fact]
    public async Task Dado_SyncPrevio_Quando_SyncSemMudancas_Entao_MarcasSkipped()
    {
        // Covers RF-004: unchanged hashes are skipped, nothing is copied again
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home);
        await service.SyncAsync([AgentType.Claude]);

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        var claude = status.Agents.Single();
        claude.Installed.ShouldBe(0);
        claude.Updated.ShouldBe(0);
        claude.Skipped.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_SkillAtualizadaNaFonte_Quando_Sync_Entao_AtualizaDestino()
    {
        // Covers RF-004: a changed skill hash triggers a copy
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home);
        await service.SyncAsync([AgentType.Claude]);

        WriteSkill(repo, "alpha-skill", "v2 content");
        await Git(repo, "add", "-A");
        await Git(repo, "commit", "-m", "update alpha");

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        var claude = status.Agents.Single();
        claude.Updated.ShouldBe(1);
        claude.Skipped.ShouldBe(1);

        var target = File.ReadAllText(Path.Join(home, ".claude", "skills", "alpha-skill", "SKILL.md"));
        target.ShouldContain("v2 content");
    }

    [Fact]
    public async Task Dado_SkillRemovidaNaFonte_Quando_Sync_Entao_MantemDestinoEMarcaManifesto()
    {
        // Covers edge case: upstream removal keeps the local copy and flags it
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home);
        await service.SyncAsync([AgentType.Claude]);

        await Git(repo, "rm", "-r", "skills/beta-skill");
        await Git(repo, "commit", "-m", "remove beta");

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        Directory.Exists(Path.Join(home, ".claude", "skills", "beta-skill")).ShouldBeTrue();

        var manifest = File.ReadAllText(Path.Join(home, ".claude", "skills", ".harness-skills.json"));
        manifest.ShouldContain("\"beta-skill\"");
        manifest.ShouldContain("\"removedFromSource\": true");
    }

    [Fact]
    public async Task Dado_RepositorioInvalido_Quando_Sync_Entao_StatusFailed()
    {
        // Covers FR-005/RNF-004: invalid repository value fails the run, not the app
        var home = Path.Join(_root, "home");
        var service = CreateService("not a repo !!!", home);

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Failed);
        status.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_AgenteComDoisDiretorios_Quando_Sync_Entao_InstalaEmAmbos()
    {
        // Covers RF-001: extra directories receive the same skills
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home);

        var status = await service.SyncAsync([AgentType.Devin]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        File.Exists(Path.Join(home, ".devin", "skills", "alpha-skill", "SKILL.md")).ShouldBeTrue();
        File.Exists(Path.Join(home, ".config", "devin", "skills", "alpha-skill", "SKILL.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_TokenConfigurado_Quando_SyncEmRepoLocal_Entao_SucessoSemAuthNaUrl()
    {
        // Covers RF-010: the token flows through -c http.extraheader scoped to
        // github.com, so a local/path repo still clones anonymously.
        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var service = CreateService(repo, home, accessToken: "ghp_test_token");

        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        File.Exists(Path.Join(home, ".claude", "skills", "alpha-skill", "SKILL.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_CacheInacessivel_Quando_Sync_Entao_RecuperaESincroniza()
    {
        // Covers RF-005 + AC1: an inaccessible cache is moved aside and a clean
        // clone drives a full sync to the agent targets
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var repo = await CreateSourceRepositoryAsync();
        var home = Path.Join(_root, "home");
        var cache = Path.Join(_root, "cache");
        Directory.CreateDirectory(cache);
        File.SetUnixFileMode(cache, 0);

        var service = CreateService(repo, home);
        var status = await service.SyncAsync([AgentType.Claude]);

        status.State.ShouldBe(SkillsSyncState.Succeeded);
        File.Exists(Path.Join(home, ".claude", "skills", "alpha-skill", "SKILL.md")).ShouldBeTrue();
        Directory.Exists(cache).ShouldBeTrue();
        var stale = Directory.EnumerateDirectories(_root, "cache.inaccessible-*").Single();
        File.SetUnixFileMode(stale,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private SkillsSyncService CreateService(string repository, string home, string? accessToken = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Taskboard:Skills:Repository", repository),
                new KeyValuePair<string, string?>("Taskboard:Skills:SyncTimeoutSeconds", "60"),
            ])
            .Build();

        return new SkillsSyncService(
            configuration,
            NullLogger<SkillsSyncService>.Instance,
            Path.Join(_root, "cache"),
            home,
            _ => Task.FromResult<IReadOnlyCollection<AgentType>>([AgentType.Claude]),
            accessToken is null ? null : _ => Task.FromResult<string?>(accessToken));
    }

    private async Task<string> CreateSourceRepositoryAsync()
    {
        var repoDir = Path.Join(_root, $"source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoDir);
        await Git(repoDir, "init");
        await Git(repoDir, "config", "user.email", "tests@taskboard.local");
        await Git(repoDir, "config", "user.name", "Taskboard Tests");
        WriteSkill(repoDir, "alpha-skill");
        WriteSkill(repoDir, "beta-skill");
        await Git(repoDir, "add", "-A");
        await Git(repoDir, "commit", "-m", "initial skills");
        return repoDir;
    }

    private static void WriteSkill(string repoDir, string name, string content = "v1 content")
    {
        var dir = Path.Join(repoDir, "skills", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Join(dir, "SKILL.md"),
            $"---\nname: {name}\ndescription: test skill\n---\n{content}\n");
    }

    private static async Task Git(string workingDir, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)} failed: {stderr}");
    }
}
