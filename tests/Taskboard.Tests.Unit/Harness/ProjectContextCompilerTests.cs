using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Harness;
using Taskboard.Integrations.Harness.Context;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class ProjectContextCompilerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ctx-{Guid.NewGuid():N}");
    private readonly string _repoPath;
    private readonly GitCommandRunner _git = new();
    private readonly FakeMemoryService _memory = new();
    private readonly ProjectContextCompiler _sut;

    public ProjectContextCompilerTests()
    {
        _repoPath = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_repoPath);
        InitRepo(_repoPath);
        _sut = new ProjectContextCompiler(_git, _memory, NullLogger<ProjectContextCompiler>.Instance);
    }

    [Fact]
    public async Task Dado_RepoComClaudeMd_Quando_Compile_Entao_PromptContemInstrucoesEEnv()
    {
        await File.WriteAllTextAsync(Path.Combine(_repoPath, "CLAUDE.md"), "# Regras\nSempre rodar dotnet test.");

        var result = await _sut.CompileAsync(_repoPath, AgentType.Claude, 32000);

        result.SystemPrompt.ShouldContain("Sempre rodar dotnet test");
        result.SystemPrompt.ShouldContain("<env>");
        result.SystemPrompt.ShouldContain($"Working directory: {_repoPath}");
        result.SystemPrompt.ShouldContain("Git branch: main");
        result.InjectedFiles.ShouldContain("CLAUDE.md");
        result.EstimatedTokens.ShouldBeGreaterThan(0);
    }

    // RF-001: AGENTS.md + CLAUDE.md idênticos não duplicam conteúdo
    [Fact]
    public async Task Dado_AgentsEClaudeIdenticos_Quando_Compile_Entao_SemDuplicacao()
    {
        var same = "# Regras\nMesma regra.";
        await File.WriteAllTextAsync(Path.Combine(_repoPath, "AGENTS.md"), same);
        await File.WriteAllTextAsync(Path.Combine(_repoPath, "CLAUDE.md"), same);

        var result = await _sut.CompileAsync(_repoPath, AgentType.Codex, 32000);

        result.InjectedFiles.ShouldContain("AGENTS.md");
        result.InjectedFiles.ShouldContain("CLAUDE.md");
        var occurrences = result.SystemPrompt.Split("Mesma regra.").Length - 1;
        occurrences.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_RulesDirESkills_Quando_Compile_Entao_IncluiAmbos()
    {
        Directory.CreateDirectory(Path.Combine(_repoPath, ".claude", "rules"));
        await File.WriteAllTextAsync(Path.Combine(_repoPath, ".claude", "rules", "global-rules.md"), "Regra global: sem secrets.");
        Directory.CreateDirectory(Path.Combine(_repoPath, ".claude", "skills", "deploy"));
        await File.WriteAllTextAsync(
            Path.Combine(_repoPath, ".claude", "skills", "deploy", "SKILL.md"),
            "---\nname: deploy\ndescription: Faz deploy seguro\n---\n# Deploy");

        var result = await _sut.CompileAsync(_repoPath, AgentType.Claude, 32000);

        result.SystemPrompt.ShouldContain("Regra global: sem secrets");
        result.SystemPrompt.ShouldContain("deploy");
        result.SystemPrompt.ShouldContain("Faz deploy seguro");
        result.InjectedFiles.ShouldContain(f => f.Contains("global-rules.md"));
    }

    // RF-004: memórias do repositório são injetadas em <project_memory>
    [Fact]
    public async Task Dado_MemoriaPersistida_Quando_Compile_Entao_BlocoProjectMemory()
    {
        _memory.Items.Add(new ProjectMemoryItemDto(
            "m1", "owner/repo", "deploy", "Sempre limpar _framework antes de publicar",
            "LessonLearned", ["deploy"], DateTime.UtcNow, DateTime.UtcNow, 1));

        var result = await _sut.CompileAsync(_repoPath, AgentType.Claude, 32000);

        result.SystemPrompt.ShouldContain("<project_memory>");
        result.SystemPrompt.ShouldContain("Sempre limpar _framework");
        result.MemoriesInjectedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_RepoSemInstrucoes_Quando_Compile_Entao_PromptMinimoComEnv()
    {
        var result = await _sut.CompileAsync(_repoPath, AgentType.OpenCode, 32000);

        result.SystemPrompt.ShouldContain("<env>");
        result.SystemPrompt.ShouldContain("Runtime:");
        result.InjectedFiles.ShouldBeEmpty();
    }

    // Guardrail: nunca injeta secrets do ambiente no prompt
    [Fact]
    public async Task Dado_EnvComSegredo_Quando_Compile_Entao_SemVazamento()
    {
        Environment.SetEnvironmentVariable("TASKBOARD_TEST_SECRET_CTX", "s3cr3t-value");
        try
        {
            var result = await _sut.CompileAsync(_repoPath, AgentType.Claude, 32000);
            result.SystemPrompt.ShouldNotContain("s3cr3t-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TASKBOARD_TEST_SECRET_CTX", null);
        }
    }

    private void InitRepo(string path)
    {
        _git.RunAsync(path, ["init", "-b", "main"]).GetAwaiter().GetResult().ExitCode.ShouldBe(0);
        _git.RunAsync(path, ["config", "user.email", "t@t"]).GetAwaiter().GetResult();
        _git.RunAsync(path, ["config", "user.name", "T"]).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(path, "README.md"), "# x\n");
        _git.RunAsync(path, ["add", "-A"]).GetAwaiter().GetResult();
        _git.RunAsync(path, ["commit", "-m", "init"]).GetAwaiter().GetResult().ExitCode.ShouldBe(0);
        _git.RunAsync(path, ["remote", "add", "origin", "https://github.com/owner/repo.git"]).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        public List<ProjectMemoryItemDto> Items { get; } = new();

        public Task<ProjectMemoryItemDto> AddMemoryAsync(
            string repositoryFullName, string topic, string content,
            IReadOnlyList<string>? tags = null, MemoryType type = MemoryType.Fact,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ProjectMemoryItemDto(
                Guid.NewGuid().ToString("N"), repositoryFullName, topic, content,
                type.ToString(), tags ?? [], DateTime.UtcNow, DateTime.UtcNow, 1));

        public Task<IReadOnlyList<ProjectMemoryItemDto>> SearchAsync(
            string repositoryFullName, string query, int take = 10,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectMemoryItemDto>>(Items.Take(take).ToList());

        public Task<IReadOnlyList<ProjectMemoryItemDto>> ListAsync(
            string repositoryFullName, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectMemoryItemDto>>(Items.Take(take).ToList());

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(m => m.Id == id);
            return Task.CompletedTask;
        }
    }
}
