using Shouldly;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20261104-agent-context-templates RF-003: named task templates.</summary>
public class AgentTemplateCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "agent-harness-templates-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Write(string file, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Dado_TemplateValido_Quando_GetAsync_Entao_ParseaFrontmatterEBody()
    {
        Write("reviewer.md", """
            ---
            name: reviewer
            description: Revisa PRs com foco em segurança
            cli: claude
            model: sonnet
            effort: high
            ---
            Você é um revisor. Aponte riscos, não reescreva.
            """);

        var catalog = new AgentTemplateCatalog(_dir);
        var template = await catalog.GetAsync("reviewer");

        template.ShouldNotBeNull();
        template.Name.ShouldBe("reviewer");
        template.Description.ShouldBe("Revisa PRs com foco em segurança");
        template.Cli.ShouldBe("claude");
        template.Model.ShouldBe("sonnet");
        template.Effort.ShouldBe("high");
        template.Body.ShouldBe("Você é um revisor. Aponte riscos, não reescreva.");
        template.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_NomeDivergente_Quando_ListAsync_Entao_MarcadoComErro()
    {
        Write("reviewer.md", "---\nname: other\n---\nbody");

        var list = await new AgentTemplateCatalog(_dir).ListAsync();
        var entry = list.ShouldHaveSingleItem();
        entry.Error.ShouldNotBeNull();
        entry.Error.ShouldContain("filename stem");
        (await new AgentTemplateCatalog(_dir).GetAsync("reviewer")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ArquivoSemFrontmatter_Quando_ListAsync_Entao_ErroIsolado()
    {
        Write("broken.md", "just a body, no frontmatter");
        Write("ok.md", "---\nname: ok\n---\nok body");

        var list = await new AgentTemplateCatalog(_dir).ListAsync();
        list.Count.ShouldBe(2);
        list.ShouldContain(t => t.Name == "broken" && t.Error != null);
        list.ShouldContain(t => t.Name == "ok" && t.Error == null && t.Body == "ok body");
    }

    [Fact]
    public async Task Dado_DirInexistente_Quando_ListAsync_Entao_ListaVazia()
    {
        var list = await new AgentTemplateCatalog(_dir).ListAsync();
        list.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_Traversal_Quando_GetAsync_Entao_Nulo()
    {
        var catalog = new AgentTemplateCatalog(_dir);
        (await catalog.GetAsync("../secret")).ShouldBeNull();
    }
}
