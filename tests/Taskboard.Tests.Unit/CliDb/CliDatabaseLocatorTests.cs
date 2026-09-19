using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.CliDb;
using Xunit;

namespace Taskboard.Tests.Unit.CliDb;

public class CliDatabaseLocatorTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "clidb-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private CliDatabaseLocator CriarLocator() =>
        new(_home, NullLogger<CliDatabaseLocator>.Instance);

    private static CliDbSource Fonte(string pattern) =>
        new("test", pattern, ["sessions"], ["credential"]);

    [Fact]
    public void Dado_ArquivoExistente_Quando_Resolver_Entao_RetornaCaminhoAbsoluto()
    {
        var db = Path.Combine(_home, "db", "app.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        File.WriteAllBytes(db, [1, 2, 3]);

        var resolved = CriarLocator().Resolve(Fonte("db/app.db"));

        resolved.ShouldHaveSingleItem().ShouldBe(db);
    }

    [Fact]
    public void Dado_ArquivoAusente_Quando_Resolver_Entao_ListaVazia()
    {
        CriarLocator().Resolve(Fonte("db/missing.db")).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_Glob_Quando_Resolver_Entao_ExpandeOrdenado()
    {
        var dir = Path.Combine(_home, "conv");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "b.db"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "a.db"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "ignore.txt"), [1]);

        var resolved = CriarLocator().Resolve(Fonte("conv/*.db"));

        resolved.Count.ShouldBe(2);
        resolved[0].ShouldEndWith("a.db");
        resolved[1].ShouldEndWith("b.db");
    }

    [Fact]
    public void Dado_PatternAbsolutoOuTraversal_Quando_Resolver_Entao_Rejeitado()
    {
        var locator = CriarLocator();
        locator.Resolve(Fonte("/etc/passwd")).ShouldBeEmpty();
        locator.Resolve(Fonte("../escape.db")).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_GetStatus_Quando_Chamar_Entao_ReportaPorFonteSemCriarDiretorios()
    {
        // Apenas um arquivo de todo o inventário existe.
        var codexDir = Path.Combine(_home, ".codex");
        Directory.CreateDirectory(codexDir);
        File.WriteAllBytes(Path.Combine(codexDir, "state_1.sqlite"), [1]);

        var status = CriarLocator().GetStatus();

        status.ShouldContain(s =>
            s.Kind == AgentCliKind.Codex && s.Status == CliDbSourceStatus.Available);
        status.ShouldContain(s =>
            s.Kind == AgentCliKind.OpenCode && s.Status == CliDbSourceStatus.Missing);

        // Nada foi criado além do que o teste criou.
        Directory.Exists(Path.Combine(_home, ".cline")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_home, ".local")).ShouldBeFalse();
    }

    [Fact]
    public void Dado_GlobSemMatches_Quando_GetStatus_Entao_Missing()
    {
        var status = CriarLocator().GetStatus();
        status.Where(s => s.Kind == AgentCliKind.Antigravity)
            .ShouldAllBe(s => s.Status == CliDbSourceStatus.Missing);
    }
}
