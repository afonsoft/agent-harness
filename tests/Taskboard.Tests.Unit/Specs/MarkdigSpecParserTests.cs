using Shouldly;
using Taskboard.Integrations.Specs;
using Taskboard.Specs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>
/// SPEC-20260919-ade-living-specs RF-001 — Markdig SDD parser over both spec
/// generations found in .specs/ (old: "SPEC Metadata" + FR-xxx; new: "Metadata"
/// + RF-xxx + BDD criteria).
/// </summary>
public class MarkdigSpecParserTests
{
    private readonly MarkdigSpecParser _parser = new();

    private const string SpecNova = """
        # SPEC-20260919-ade-cockpit-hitl

        ## 0. Metadata

        | Field | Value |
        | --- | --- |
        | Feature | `ade-cockpit-hitl` |
        | Type | `Feature` |
        | Status | `Approved` |
        | Ticket | [#170](https://github.com/x/y/issues/170) |

        ## 4. Requirements

        ### RF-001: Painel de controle
        - **Description:** texto qualquer

        ### RF-002 — Gate de aprovação
        - **Description:** texto

        ## 6. Acceptance Criteria

        - [ ] **Given** x, **when** y, **then** z.
        - [ ] **Given** a, **when** b, **then** c.
        - [x] **Given** done, **when** checked, **then** ok.

        ## 7. Task Plan

        - [ ] **T1 — Parser:** fazer
        - [x] **T2 — API:** feito
        """;

    private const string SpecAntiga = """
        # SPEC-001: Visão Geral

        ## 0. SPEC Metadata

        | Field | Value |
        |---|---|
        | Feature name | Domain Model |
        | Status | Implemented |
        | Date | 2026-08-31 |

        ## 6. Functional Requirements

        ### FR-001: Status e prioridades tipadas
        texto

        ### FR-002: Ciclo de vida da tarefa
        texto
        """;

    [Fact]
    public void Dado_SpecNova_Quando_Parse_Entao_MetadadosERequisitos()
    {
        var spec = _parser.Parse(".specs/SPEC-20260919-ade-cockpit-hitl.md", SpecNova);

        spec.Id.ShouldBe("SPEC-20260919-ade-cockpit-hitl");
        spec.Status.ShouldBe(SpecStatus.Approved);
        spec.Type.ShouldBe("Feature");
        spec.Requirements.Select(r => r.Code).ShouldBe(["RF-001", "RF-002"]);
        spec.Requirements[1].Title.ShouldContain("Gate de aprovação");
        spec.AcceptanceCriteria.Count.ShouldBe(3);
        spec.Tasks.Count.ShouldBe(2);
        spec.Tasks.Count(t => t.Done).ShouldBe(1);
        spec.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SpecAntiga_Quando_Parse_Entao_FrERequisitosEStatusNormalizado()
    {
        var spec = _parser.Parse(".specs/SPEC-001-domain-model.md", SpecAntiga);

        spec.Id.ShouldBe("SPEC-001-domain-model");
        spec.Status.ShouldBe(SpecStatus.Done);
        spec.RawStatus.ShouldBe("Implemented");
        spec.Date.ShouldBe(new DateOnly(2026, 8, 31));
        spec.Requirements.Select(r => r.Code).ShouldBe(["FR-001", "FR-002"]);
    }

    [Theory]
    [InlineData("`Done` — entregue via PR #94 (merged)", SpecStatus.Done)]
    [InlineData("Completed", SpecStatus.Done)]
    [InlineData("`Approved`", SpecStatus.Approved)]
    [InlineData("Draft", SpecStatus.Draft)]
    [InlineData("`InImplementation`", SpecStatus.InImplementation)]
    [InlineData("Deprecated", SpecStatus.Deprecated)]
    public void Dado_StatusComAnotacoes_Quando_Parse_Entao_Normaliza(string raw, SpecStatus expected)
    {
        var md = $"# S\n\n## 0. Metadata\n\n| Field | Value |\n|---|---|\n| Status | {raw} |\n";

        _parser.Parse(".specs/SPEC-1-x.md", md).Status.ShouldBe(expected);
    }

    [Fact]
    public void Dado_StatusDesconhecido_Quando_Parse_Entao_LintWarning()
    {
        var md = "# S\n\n## 0. Metadata\n\n| Field | Value |\n|---|---|\n| Status | `Em revisão` |\n";

        var spec = _parser.Parse(".specs/SPEC-1-x.md", md);

        spec.Status.ShouldBe(SpecStatus.Draft);
        spec.Warnings.ShouldContain(w => w.Code == "UNKNOWN_STATUS");
    }

    [Fact]
    public void Dado_SpecSemTabelaDeMetadados_Quando_Parse_Entao_WarningSemExcecao()
    {
        var spec = _parser.Parse(".specs/SPEC-1-x.md", "# titulo solto\n\ntexto\n");

        spec.Status.ShouldBe(SpecStatus.Draft);
        spec.Warnings.ShouldContain(w => w.Code == "MISSING_METADATA");
    }

    [Fact]
    public void Dado_CorpusReal_Quando_ParseTodas_Entao_SemExcecaoEComStatus()
    {
        var dir = FindRepoRoot();
        var files = Directory.GetFiles(Path.Combine(dir, ".specs"), "SPEC-*.md");
        files.Length.ShouldBeGreaterThan(50);

        var unknown = new List<string>();
        foreach (var file in files)
        {
            var spec = _parser.Parse(file, File.ReadAllText(file));
            spec.Id.ShouldBe(Path.GetFileNameWithoutExtension(file));
            if (spec.Warnings.Any(w => w.Code == "UNKNOWN_STATUS"))
            {
                unknown.Add($"{spec.Id}: {spec.RawStatus}");
            }
        }

        unknown.ShouldBeEmpty();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".specs")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull(".specs directory not found above test output");
        return dir.FullName;
    }

    [Fact]
    public void Dado_SecaoFilesToCreate_Quando_Parse_Entao_ExtraiPaths()
    {
        var md = """
            # S

            ## 0. Metadata

            | Field | Value |
            |---|---|
            | Status | `Approved` |

            ### Files to create or modify

            ```text
            src/Taskboard.Domain/Specs/Foo.cs          [new]
            src/Taskboard.Server/Program.cs            [modified]
            ```
            """;

        var spec = _parser.Parse(".specs/SPEC-1-x.md", md);

        spec.ReferencedFiles.ShouldBe(
            ["src/Taskboard.Domain/Specs/Foo.cs", "src/Taskboard.Server/Program.cs"]);
    }
}
