using System;
using System.IO;
using System.Threading.Tasks;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Integrations.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Skills;

public class SkillDiscoveryServiceTests
{
    [Fact]
    public async Task Dado_DiretorioComSkill_Quando_Descobrir_Entao_RetornaSkillComNomeEDescricao()
    {
        var temp = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        var skillDir = Path.Join(temp, "manage-taskboard");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Join(skillDir, "SKILL.md"),
            "---\nname: manage-taskboard\ndescription: Gerencia o taskboard.\n---\n");

        var service = new SkillDiscoveryService([new SkillDiscoverySource("custom", temp)]);

        var skills = await service.DiscoverAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("manage-taskboard");
        skills[0].Description.ShouldBe("Gerencia o taskboard.");
        skills[0].Source.ShouldBe("custom");
        skills[0].Path.ShouldBe(skillDir);
    }

    [Fact]
    public async Task Dado_SkillComArquivos_Quando_Detalhar_Entao_FilesListaTodosOsArquivos()
    {
        // Covers RF-006: detail DTO exposes the skill file tree
        var (service, skillDir) = CreateServiceWithSkill(("references/cli.md", "# CLI"), ("scripts/run.sh", "#!/bin/sh"));
        var detail = await service.GetDetailAsync("custom", "manage-taskboard");

        detail.ShouldNotBeNull();
        detail.Files.Select(f => f.RelativePath).ShouldBe(["SKILL.md", "references/cli.md", "scripts/run.sh"]);
        detail.Files.All(f => f.IsText).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_SkillComArquivoOculto_Quando_Detalhar_Entao_FilesIgnoraOcultos()
    {
        // Covers RF-006: hidden directories/files are skipped
        var (service, _) = CreateServiceWithSkill((".git/config", "x"), ("docs/guide.md", "g"));
        var detail = await service.GetDetailAsync("custom", "manage-taskboard");

        detail.ShouldNotBeNull();
        detail.Files.Select(f => f.RelativePath).ShouldBe(["SKILL.md", "docs/guide.md"]);
    }

    [Fact]
    public async Task Dado_ArquivoDeTextoValido_Quando_LerArquivo_Entao_RetornaConteudo()
    {
        // Covers RF-007: valid text file returns content
        var (service, _) = CreateServiceWithSkill(("references/cli.md", "# CLI reference"));
        var result = await service.GetFileAsync("custom", "manage-taskboard", "references/cli.md");

        result.Error.ShouldBe(SkillFileError.None);
        result.Content.ShouldBe("# CLI reference");
        result.RelativePath.ShouldBe("references/cli.md");
    }

    [Theory]
    [InlineData("../admin.json")]
    [InlineData("references/../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("references//cli.md")]
    [InlineData("..%2Fadmin.json")]
    public async Task Dado_PathInvalido_Quando_LerArquivo_Entao_RetornaInvalidPath(string relativePath)
    {
        // Covers RF-007: traversal/absolute/empty-segment paths rejected with InvalidPath
        var (service, _) = CreateServiceWithSkill(("references/cli.md", "x"));
        var result = await service.GetFileAsync("custom", "manage-taskboard", relativePath);

        result.Error.ShouldBe(SkillFileError.InvalidPath);
        result.Content.ShouldBeNull();
    }

    [Theory]
    [InlineData("missing.md")]
    [InlineData(".git/config")]
    public async Task Dado_ArquivoInexistenteOuOculto_Quando_LerArquivo_Entao_RetornaNotFound(string relativePath)
    {
        // Covers RF-007: missing files and hidden segments return NotFound
        var (service, _) = CreateServiceWithSkill(("references/cli.md", "x"));
        var result = await service.GetFileAsync("custom", "manage-taskboard", relativePath);

        result.Error.ShouldBe(SkillFileError.NotFound);
    }

    [Fact]
    public async Task Dado_ArquivoNaoTexto_Quando_LerArquivo_Entao_RetornaNotText()
    {
        // Covers RF-007: non-whitelisted extension returns NotText
        var (service, _) = CreateServiceWithSkill(("assets/logo.png", new byte[] { 1, 2, 3 }));
        var result = await service.GetFileAsync("custom", "manage-taskboard", "assets/logo.png");

        result.Error.ShouldBe(SkillFileError.NotText);
    }

    [Fact]
    public async Task Dado_ArquivoAcimaDoLimite_Quando_LerArquivo_Entao_RetornaTooLarge()
    {
        // Covers RF-007: files over 256 KB return TooLarge
        var (service, _) = CreateServiceWithSkill(("docs/big.md", new string('x', 257 * 1024)));
        var result = await service.GetFileAsync("custom", "manage-taskboard", "docs/big.md");

        result.Error.ShouldBe(SkillFileError.TooLarge);
    }

    [Fact]
    public async Task Dado_SkillInexistente_Quando_LerArquivo_Entao_RetornaNotFound()
    {
        // Covers RF-007: unknown skill returns NotFound
        var (service, _) = CreateServiceWithSkill(("references/cli.md", "x"));
        var result = await service.GetFileAsync("custom", "unknown-skill", "SKILL.md");

        result.Error.ShouldBe(SkillFileError.NotFound);
    }

    [Fact]
    public async Task Dado_SymlinkForaDoDiretorio_Quando_LerArquivo_Entao_RetornaNotFound()
    {
        // Covers RF-007: symlink escaping the skill directory is rejected
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var (service, skillDir) = CreateServiceWithSkill(("references/cli.md", "x"));
        var outside = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(outside, "secret");
        File.CreateSymbolicLink(Path.Join(skillDir, "link.md"), outside);

        var result = await service.GetFileAsync("custom", "manage-taskboard", "link.md");

        result.Error.ShouldBe(SkillFileError.NotFound);
    }

    [Fact]
    public async Task Dado_SymlinkDeDiretorioParaFora_Quando_LerArquivo_Entao_RetornaNotFound()
    {
        // Covers RF-007: symlinked directory escaping the skill root is rejected
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var outsideDir = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid()}");
        Directory.CreateDirectory(outsideDir);
        await File.WriteAllTextAsync(Path.Join(outsideDir, "secret.md"), "secret");

        var (service, skillDir) = CreateServiceWithSkill(("references/cli.md", "x"));
        Directory.CreateSymbolicLink(Path.Join(skillDir, "linked"), outsideDir);

        var result = await service.GetFileAsync("custom", "manage-taskboard", "linked/secret.md");

        result.Error.ShouldBe(SkillFileError.NotFound);
    }

    private static (SkillDiscoveryService Service, string SkillDir) CreateServiceWithSkill(
        params (string RelativePath, object Content)[] extraFiles)
    {
        var temp = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        var skillDir = Path.Join(temp, "manage-taskboard");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Join(skillDir, "SKILL.md"),
            "---\nname: manage-taskboard\ndescription: Gerencia o taskboard.\n---\n");

        foreach (var (relativePath, content) in extraFiles)
        {
            var fullPath = Path.Join(skillDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (content is byte[] bytes)
            {
                File.WriteAllBytes(fullPath, bytes);
            }
            else
            {
                File.WriteAllText(fullPath, (string)content);
            }
        }

        return (new SkillDiscoveryService([new SkillDiscoverySource("custom", temp)]), skillDir);
    }
}
