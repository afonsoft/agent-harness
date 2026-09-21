using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Integrations.Specs;
using Taskboard.Specs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260919-ade-living-specs RF-002 — spec drift detection.</summary>
public class SpecDriftDetectorTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _specsDir;
    private readonly string _workspaceRoot;

    public SpecDriftDetectorTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "repo-" + Guid.NewGuid().ToString("N"));
        _specsDir = Path.Combine(_repoRoot, ".specs");
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_specsDir);
        Directory.CreateDirectory(_workspaceRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
        Directory.Delete(_workspaceRoot, recursive: true);
    }

    /// <summary>Minimal stand-in for WorkspaceService — &lt;root&gt;/&lt;repo-name&gt; when cloned.</summary>
    private sealed class FakeWorkspaceResolver(string root) : IWorkspacePathResolver
    {
        public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
        {
            var name = repositoryFullName?.Split('/')[^1] ?? string.Empty;
            var dir = Path.Combine(root, name);
            exists = !string.IsNullOrEmpty(name) && Directory.Exists(dir);
            return exists ? dir : root;
        }
    }

    private SpecDriftDetector Criar() =>
        new(new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = _specsDir
                })
                .Build(),
            new FakeWorkspaceResolver(_workspaceRoot));

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
                .Build(),
            new FakeWorkspaceResolver(_workspaceRoot));

        var report = await detector.BuildReportAsync();

        report.TotalSpecs.ShouldBe(0);
    }

    // SPEC-20260920-global-repo-selector RF-005 — on-demand drift per repo clone.

    private void ClonarRepoComSpec(string repoName, string specNome, string status, params string[] files)
    {
        var specsDir = Path.Combine(_workspaceRoot, repoName, ".specs");
        Directory.CreateDirectory(specsDir);
        var filesBlock = files.Length == 0
            ? ""
            : "### Files to create or modify\n\n```text\n"
                + string.Join('\n', files.Select(f => f + "   [new]"))
                + "\n```\n";
        File.WriteAllText(Path.Combine(specsDir, specNome + ".md"), $"""
            # {specNome}

            ## 0. Metadata

            | Field | Value |
            |---|---|
            | Status | `{status}` |

            ## 3. Technical Context

            {filesBlock}
            """);
    }

    [Fact]
    public async Task Dado_RepoClonadoComSpecs_Quando_DriftComRepo_Entao_ScanDoClone()
    {
        ClonarRepoComSpec("myrepo", "SPEC-9-repo", "Approved", "src/Foo.cs");
        var srcDir = Path.Combine(_workspaceRoot, "myrepo", "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "Foo.cs"), "// existe");

        var report = await Criar().BuildReportAsync("owner/myrepo");

        report.TotalSpecs.ShouldBe(1);
        report.StaleSpecsCount.ShouldBe(1);
        report.DriftItems.Single().SpecId.ShouldBe("SPEC-9-repo");
    }

    [Fact]
    public async Task Dado_RepoSemClone_Quando_DriftComRepo_Entao_RelatorioVazio()
    {
        var report = await Criar().BuildReportAsync("owner/fantasma");

        report.TotalSpecs.ShouldBe(0);
        report.DriftItems.ShouldBeEmpty();
    }
}
