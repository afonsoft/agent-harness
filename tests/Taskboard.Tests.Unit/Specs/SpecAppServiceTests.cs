using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Specs;
using Taskboard.Integrations.Specs;
using Taskboard.Specs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260919-ade-living-specs §5/§8 — file-backed spec catalog.</summary>
public class SpecAppServiceTests : IDisposable
{
    private readonly string _dir;

    public SpecAppServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SpecAppService CriarService() =>
        new(
            new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = _dir
                })
                .Build(),
            NullLogger<SpecAppService>.Instance);

    private string EscreverSpec(string nome, string status = "`Draft`")
    {
        var path = Path.Combine(_dir, nome + ".md");
        File.WriteAllText(path, $"""
            # {nome}

            ## 0. Metadata

            | Field | Value |
            |---|---|
            | Status | {status} |
            | Feature | `x` |

            ## 4. Requirements

            ### RF-001: algo
            """);
        return path;
    }

    [Fact]
    public async Task Dado_DuasSpecs_Quando_List_Entao_RetornaDtos()
    {
        EscreverSpec("SPEC-1-a", "`Draft`");
        EscreverSpec("SPEC-2-b", "`Done`");

        var lista = await CriarService().ListAsync(null, null);

        lista.Count.ShouldBe(2);
        lista.Select(s => s.Id).ShouldBe(["SPEC-2-b", "SPEC-1-a"]);
    }

    [Fact]
    public async Task Dado_FiltroStatus_Quando_List_Entao_SoDoStatus()
    {
        EscreverSpec("SPEC-1-a", "`Draft`");
        EscreverSpec("SPEC-2-b", "`Done`");

        var lista = await CriarService().ListAsync("Done", null);

        lista.Select(s => s.Id).ShouldBe(["SPEC-2-b"]);
    }

    [Fact]
    public async Task Dado_BuscaPorTexto_Quando_List_Entao_FiltraPorIdOuTitulo()
    {
        EscreverSpec("SPEC-1-alpha", "`Draft`");
        EscreverSpec("SPEC-2-beta", "`Draft`");

        var lista = await CriarService().ListAsync(null, "ALPHA");

        lista.Select(s => s.Id).ShouldBe(["SPEC-1-alpha"]);
    }

    [Fact]
    public async Task Dado_SpecExistente_Quando_UpdateStatus_Entao_ReescreveSoACelula()
    {
        var path = EscreverSpec("SPEC-1-a", "`Draft`");
        var original = await File.ReadAllTextAsync(path);

        var dto = await CriarService().UpdateStatusAsync("SPEC-1-a", "Approved");

        dto!.Status.ShouldBe("Approved");
        var novo = await File.ReadAllTextAsync(path);
        novo.ShouldContain("| Status | `Approved` |");
        novo.ShouldContain("### RF-001: algo");
        novo.Length.ShouldBe(original.Length + "Approved".Length - "Draft".Length);
    }

    [Fact]
    public async Task Dado_StatusInvalido_Quando_UpdateStatus_Entao_400()
    {
        EscreverSpec("SPEC-1-a");

        var ex = await Should.ThrowAsync<DomainException>(
            () => CriarService().UpdateStatusAsync("SPEC-1-a", "Inexistente"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidSpecStatus);
    }

    [Fact]
    public async Task Dado_SpecInexistente_Quando_UpdateStatus_Entao_Null()
    {
        var dto = await CriarService().UpdateStatusAsync("SPEC-9-x", "Done");

        dto.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_SpecInexistente_Quando_Get_Entao_Null()
    {
        (await CriarService().GetAsync("SPEC-9-x")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_DirInexistente_Quando_List_Entao_Vazio()
    {
        var service = new SpecAppService(
            new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = Path.Combine(_dir, "nao-existe")
                })
                .Build(),
            NullLogger<SpecAppService>.Instance);

        (await service.ListAsync(null, null)).ShouldBeEmpty();
    }
}
