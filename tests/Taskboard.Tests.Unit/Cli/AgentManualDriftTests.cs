using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using CliProgram = Taskboard.Cli.Program;

namespace Taskboard.Tests.Unit.Cli;

/// <summary>
/// SPEC-20261104-cli-agent-manual RF-003: the embedded manual is the single
/// source of truth for the taskctl surface — these checks break the build
/// when the manual, the command tree, or the skill stub drift apart.
/// </summary>
public partial class AgentManualDriftTests
{
    private static readonly string Manual = Taskboard.Cli.AgentManual.Render();
    private static readonly IReadOnlyList<string> Registered = Taskboard.Cli.AgentManual.RegisteredCommands();

    // taskctl <token> tokens that are legitimately not command names.
    private static readonly HashSet<string> AllowedNonCommands = new(StringComparer.Ordinal)
    {
        "--skill", "--agent-manual", "--json", "--help", "<command>",
    };

    private static IEnumerable<string> ManualCommandTokens()
    {
        foreach (Match m in TaskctlTokenRegex().Matches(Manual))
        {
            var token = m.Groups[1].Value;
            if (!AllowedNonCommands.Contains(token) && !token.StartsWith('-'))
            {
                yield return token;
            }
        }
    }

    [Fact]
    public void Dado_Manual_Quando_ExtraiComandos_Entao_TodoTokenResolvidoNaArvore()
    {
        var tokens = ManualCommandTokens().Distinct().ToList();
        tokens.ShouldNotBeEmpty("o manual deve referenciar comandos taskctl");

        var naoRegistrados = tokens.Where(t => !Registered.Contains(t)).ToList();
        naoRegistrados.ShouldBeEmpty(
            $"comandos citados no manual que não existem na árvore: {string.Join(", ", naoRegistrados)}");
    }

    [Fact]
    public void Dado_ComandosRegistrados_Quando_ComparadosAoManual_Entao_TodosAparecem()
    {
        Registered.ShouldNotBeEmpty();

        var ausentes = Registered
            .Where(c => !ManualCommandTokens().Contains(c) && !Manual.Contains($"taskctl {c}"))
            .ToList();
        ausentes.ShouldBeEmpty(
            $"comandos registrados sem documentação no manual: {string.Join(", ", ausentes)}");
    }

    [Fact]
    public void Dado_ManifestJson_Quando_Gerado_Entao_ComandosBatemComArvore()
    {
        var manifest = Taskboard.Cli.AgentManual.Manifest();
        var commands = manifest["commands"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToList();

        commands.ShouldBeEquivalentTo(Registered.ToList());
        manifest["version"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Dado_SkillStub_Quando_Lido_Entao_ApostaEmSkillFlagESemTabelasDeComando()
    {
        var stub = LerSkillStub();
        stub.ShouldNotBeNull("manage-taskboard/SKILL.md não encontrado");

        stub.ShouldContain("--skill");

        var tabelasStale = Registered
            .Where(c => c != "skill" && stub.Contains($"taskctl {c}", StringComparison.Ordinal))
            .ToList();
        tabelasStale.ShouldBeEmpty(
            $"a skill stub deve deferir para --skill, sem tabelas de comandos: {string.Join(", ", tabelasStale)}");
    }

    [Fact]
    public void Dado_Manual_Quando_Carregado_Entao_TamanhoDentroDoLimite()
    {
        // Convenção: ferramentas de leitura cortam perto de ~50KB.
        System.Text.Encoding.UTF8.GetByteCount(Manual).ShouldBeLessThan(48 * 1024);
    }

    [Fact]
    public void Dado_Manual_Quando_Carregado_Entao_SecoesObrigatoriasPresentes()
    {
        foreach (var secao in new[] { "## Commands", "## Exit codes", "## Environment", "## Workflow contract" })
        {
            Manual.ShouldContain(secao);
        }
    }

    [Fact]
    public async Task Dado_SkillInvocation_Quando_Executada_Entao_ImprimeManualOffline()
    {
        var saida = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(saida);
            var code = await CliProgram.Main(["--skill"]);
            code.ShouldBe(0);
        }
        finally
        {
            Console.SetOut(original);
        }

        saida.ToString().ShouldContain("# taskctl — agent manual");
    }

    [Fact]
    public void Dado_ArgumentosInvalidos_Quando_SkillInvocation_Entao_NaoIntercepta()
    {
        Taskboard.Cli.AgentManual.IsSkillInvocation(["--skill", "run"]).ShouldBeFalse();
        Taskboard.Cli.AgentManual.IsSkillInvocation(["run", "--skill"]).ShouldBeFalse();
        Taskboard.Cli.AgentManual.IsSkillInvocation(["--skill", "--json"]).ShouldBeTrue();
        Taskboard.Cli.AgentManual.IsSkillInvocation(["--json", "--skill"]).ShouldBeTrue();
        Taskboard.Cli.AgentManual.IsSkillInvocation(["--agent-manual"]).ShouldBeTrue();
    }

    private static string? LerSkillStub()
    {
        // O csproj copia .claude/skills para o output (skills/<name>/SKILL.md).
        var noOutput = Path.Combine(AppContext.BaseDirectory, "skills", "manage-taskboard", "SKILL.md");
        if (File.Exists(noOutput))
        {
            return File.ReadAllText(noOutput);
        }

        // Fallback: sobe até a raiz do repo procurando .claude/skills.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, ".claude", "skills", "manage-taskboard", "SKILL.md");
            if (File.Exists(candidato))
            {
                return File.ReadAllText(candidato);
            }

            dir = dir.Parent;
        }

        return null;
    }

    [GeneratedRegex(@"taskctl\s+([a-zA-Z][a-zA-Z0-9:_-]*|--[a-z-]+|<command>)")]
    private static partial Regex TaskctlTokenRegex();
}
