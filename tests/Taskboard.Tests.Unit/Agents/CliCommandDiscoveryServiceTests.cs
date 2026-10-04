using Shouldly;
using Taskboard.Integrations.Commands;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-001/RF-002: discovery de slash commands
/// e skills por CLI — varre os dirs convencionais de cada CLI sob $HOME e
/// mescla com os SKILL.md do diretório de skills da CLI.
/// </summary>
public sealed class CliCommandDiscoveryServiceTests : IDisposable
{
    private readonly string _home;

    public CliCommandDiscoveryServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"cli-cmds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private CliCommandDiscoveryService Sut() => new(_home);

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_home, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Dado_DirClaudeCommands_Quando_ListAsync_Entao_RetornaComandos()
    {
        WriteFile(".claude/commands/review.md", """
            ---
            description: Revisa o diff atual contra a base
            argument-hint: base branch
            ---
            Review the current diff.
            """);
        WriteFile(".claude/commands/plan.md", "Draft the implementation plan.");
        WriteFile(".claude/commands/ops/deploy.md", "Deploy to prod."); // nested → name "ops/deploy"? convention: claude namespaces = subdir

        var items = await Sut().ListAsync("Claude");

        var review = items.Where(i => i.Name == "review").ShouldHaveSingleItem();
        review.Description.ShouldBe("Revisa o diff atual contra a base");
        review.Kind.ShouldBe("command");
        review.Source.ShouldBe("claude");
        items.ShouldContain(i => i.Name == "plan" && i.Description == "Draft the implementation plan.");
        items.ShouldContain(i => i.Name == "ops/deploy");
    }

    [Fact]
    public async Task Dado_CodexPrompts_Quando_ListAsync_Entao_LeiaDotCodex()
    {
        WriteFile(".codex/prompts/fix-bug.md", "Find and fix the bug in $ARGUMENTS.");

        var items = await Sut().ListAsync("codex");

        var cmd = items.Where(i => i.Name == "fix-bug").ShouldHaveSingleItem();
        cmd.Source.ShouldBe("codex");
    }

    [Fact]
    public async Task Dado_OpenCodeDirs_Quando_ListAsync_Entao_MesclaAmbosDiretorios()
    {
        WriteFile(".config/opencode/commands/init.md", "Bootstrap AGENTS.md.");
        WriteFile(".opencode/commands/cleanup.md", "Clean dead code.");

        var items = await Sut().ListAsync("OpenCode");

        items.Select(i => i.Name).ShouldBe(["cleanup", "init"], ignoreOrder: true);
        items.ShouldAllBe(i => i.Source == "opencode");
    }

    [Fact]
    public async Task Dado_SkillDirDoCli_Quando_ListAsync_Entao_IncluiSkillsComKindSkill()
    {
        WriteFile(".claude/skills/deploy/SKILL.md", """
            ---
            name: deploy
            description: Deploy the app
            ---
            Steps to deploy.
            """);
        WriteFile(".claude/commands/commit.md", "Write a commit.");

        var items = await Sut().ListAsync("Claude");

        var deploy = items.Where(i => i.Name == "deploy").ShouldHaveSingleItem();
        deploy.Kind.ShouldBe("skill");
        items.ShouldContain(i => i.Name == "commit" && i.Kind == "command");
    }

    [Fact]
    public async Task Dado_MesmoNomeEmCommandESkill_Quando_ListAsync_Entao_CommandVence()
    {
        WriteFile(".claude/commands/plan.md", "Plan body.");
        WriteFile(".claude/skills/plan/SKILL.md", """
            ---
            name: plan
            description: plan skill
            ---
            Skill body.
            """);

        var items = await Sut().ListAsync("Claude");

        var item = items.Where(i => i.Name == "plan").ShouldHaveSingleItem();
        item.Kind.ShouldBe("command");
    }

    [Fact]
    public async Task Dado_ComandoComFrontmatter_Quando_GetAsync_Entao_BodySemFrontmatterEComHint()
    {
        WriteFile(".claude/commands/review.md", """
            ---
            description: Revisa o diff
            argument-hint: base branch
            ---
            Review the current diff against $ARGUMENTS.
            """);

        var detail = await Sut().GetAsync("Claude", "review");

        detail.ShouldNotBeNull();
        detail.Body.Trim().ShouldBe("Review the current diff against $ARGUMENTS.");
        detail.ArgumentHint.ShouldBe("base branch");
        detail.Kind.ShouldBe("command");
    }

    [Fact]
    public async Task Dado_SkillDoCli_Quando_GetAsync_Entao_BodyDoSkillMd()
    {
        WriteFile(".devin/skills/deploy/SKILL.md", """
            ---
            name: deploy
            description: Deploy steps
            ---
            Do the deploy.
            """);

        var detail = await Sut().GetAsync("devin", "deploy");

        detail.ShouldNotBeNull();
        detail.Kind.ShouldBe("skill");
        detail.Body.ShouldContain("Do the deploy.");
    }

    [Fact]
    public async Task Dado_CliDesconhecida_Quando_ListAsync_Entao_ListaVazia()
    {
        WriteFile(".claude/commands/review.md", "Review.");

        var items = await Sut().ListAsync("frobnicator");

        items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_CliSemDirs_Quando_ListAsync_Entao_ListaVazia()
    {
        WriteFile(".claude/commands/review.md", "Review.");

        var items = await Sut().ListAsync("Grok");

        items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_NomeInexistente_Quando_GetAsync_Entao_Null()
    {
        (await Sut().GetAsync("Claude", "missing")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_AgentTypeNome_Quando_ListAsync_Entao_MapeiaParaKind()
    {
        WriteFile(".codex/prompts/fix.md", "Fix it.");

        // AgentType "Codex" — mesmo nome do kind, mas o contrato aceita ambos.
        var items = await Sut().ListAsync("Codex");

        items.Where(i => i.Name == "fix").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dado_ArquivoNaoMd_Quando_ListAsync_Entao_Ignora()
    {
        WriteFile(".claude/commands/note.txt", "not a command");
        WriteFile(".claude/commands/real.md", "a command");

        var items = await Sut().ListAsync("Claude");

        items.Where(i => i.Name == "real").ShouldHaveSingleItem();
    }
}
