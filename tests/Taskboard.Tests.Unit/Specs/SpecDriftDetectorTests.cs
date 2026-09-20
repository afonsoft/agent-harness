using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Integrations.Specs;
using Taskboard.Specs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260919-ade-living-specs RF-002 — spec drift detection.</summary>
public class SpecDriftDetectorTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _specsDir;

    public SpecDriftDetectorTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "repo-" + Guid.NewGuid().ToString("N"));
        _specsDir = Path.Combine(_repoRoot, ".specs");
        Directory.CreateDirectory(_specsDir);
    }

    public void Dispose() => Directory.Delete(_repoRoot, recursive: true);

    private SpecDriftDetector Criar() =>
        new(new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = _specsDir
                })
                .Build());

    private void EscreverSpec(string nome, string status, params string[] files)
    {
        var filesBlock = files.Length == 0
            ? ""
            : "### Files to create or modify\n\n```text\n"
                + string.Join('\n', files.Select(f => f + "   [new]"))
                + "\n```\n";
        File.WriteAllText(Path.Combine(_specsDir, nome + ".md"), $"""
            # {nome}

            ## 0. Metadata

            | Field | Value |
            |---|---|
            | Status | `{status}` |

            ## 3. Technical Context

            {filesBlock}
            """);
    }

    private void TocarArquivo(string relPath)
    {
        var full = Path.Combine(_repoRoot, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "// existe");
    }

    [Fact]
    public async Task Dado_SpecApprovedComTodosArquivos_Quando_Drift_Entao_SugereDone()
    {
        EscreverSpec("SPEC-1-x", "Approved", "src/Foo.cs", "src/Bar.cs");
        TocarArquivo("src/Foo.cs");
        TocarArquivo("src/Bar.cs");

        var report = await Criar().BuildReportAsync();

        report.TotalSpecs.ShouldBe(1);
        report.StaleSpecsCount.ShouldBe(1);
        var item = report.DriftItems.Single();
        item.SpecId.ShouldBe("SPEC-1-x");
        item.CurrentStatus.ShouldBe("Approved");
        item.SuggestedStatus.ShouldBe("Done");
        item.MissingFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SpecApprovedComArquivoFaltando_Quando_Drift_Entao_SemSugestao()
    {
        EscreverSpec("SPEC-1-x", "Approved", "src/Foo.cs", "src/Falta.cs");
        TocarArquivo("src/Foo.cs");

        var report = await Criar().BuildReportAsync();

        report.StaleSpecsCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_SpecDoneComArquivoRemovido_Quando_Drift_Entao_SugereDeprecated()
    {
        EscreverSpec("SPEC-1-x", "Done", "src/Removido.cs", "src/Existe.cs");
        TocarArquivo("src/Existe.cs");

        var report = await Criar().BuildReportAsync();

        var item = report.DriftItems.Single();
        item.SuggestedStatus.ShouldBe("Deprecated");
        item.MissingFiles.ShouldBe(["src/Removido.cs"]);
    }

    [Fact]
    public async Task Dado_SpecSemArquivosReferenciados_Quando_Drift_Entao_Ignorada()
    {
        EscreverSpec("SPEC-1-x", "Approved");

        var report = await Criar().BuildReportAsync();

        report.TotalSpecs.ShouldBe(1);
        report.StaleSpecsCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_SpecsDirInexistente_Quando_Drift_Entao_RelatorioVazio()
    {
        var detector = new SpecDriftDetector(
            new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = Path.Combine(_repoRoot, "vazio")
                })
                .Build());

        var report = await detector.BuildReportAsync();

        report.TotalSpecs.ShouldBe(0);
    }
}
