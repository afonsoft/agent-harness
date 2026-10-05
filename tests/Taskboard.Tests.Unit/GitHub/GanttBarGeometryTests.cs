using Shouldly;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.GitHub;

/// <summary>
/// SPEC-20261010-gantt-bar-clamp: regressão do crash
/// <c>Argument_MinMaxValue</c> — Math.Clamp(largura, 1, 100 − left) quebrava
/// quando a issue era criada no último dia da janela (left = 100 → max = 0).
/// </summary>
public class GanttBarGeometryTests
{
    private static readonly DateTime RangeEnd = new(2026, 10, 5);
    private static readonly DateTime RangeStart = RangeEnd.AddDays(-90);

    [Fact]
    public void Dado_IssueCriadaNoUltimoDiaDaJanela_Quando_BarPercent_Entao_NaoLancaEFicaDentroDaTrilha()
    {
        // Reprodução do crash: created == rangeEnd → left = 100 → max = 0.
        var (left, width) = GanttBarGeometry.BarPercent(
            RangeStart, RangeEnd, RangeEnd, closedUtcDate: null);

        left.ShouldBeGreaterThanOrEqualTo(0);
        width.ShouldBeGreaterThanOrEqualTo(1);
        (left + width).ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void Dado_IssueCriadaDepoisDoRangeEnd_Quando_BarPercent_Entao_BarraPermaneceNaTrilha()
    {
        var created = RangeEnd.AddDays(3);
        var (left, width) = GanttBarGeometry.BarPercent(RangeStart, RangeEnd, created, null);

        (left + width).ShouldBeLessThanOrEqualTo(100);
        width.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Dado_IssueAbertaAntiga_Quando_BarPercent_Entao_LarguraRespeitaOBordoDireito()
    {
        // created antes do início → left = 0; aberta → end = rangeEnd.
        var (left, width) = GanttBarGeometry.BarPercent(
            RangeStart, RangeEnd, RangeStart.AddDays(-30), null);

        left.ShouldBe(0);
        width.ShouldBe(100);
    }

    [Fact]
    public void Dado_IssueFechadaNoMeioDaJanela_Quando_BarPercent_Entao_GeometriaProporcional()
    {
        var created = RangeStart.AddDays(45);
        var closed = RangeStart.AddDays(90);

        var (left, width) = GanttBarGeometry.BarPercent(RangeStart, RangeEnd, created, closed);

        left.ShouldBe(50, 0.001);
        width.ShouldBe(50, 0.001);
    }

    [Fact]
    public void Dado_IssueComFimAlemDoRangeEnd_Quando_BarPercent_Entao_FimClampadoNoBordo()
    {
        var created = RangeStart.AddDays(45);
        var closed = RangeEnd.AddDays(30);

        var (left, width) = GanttBarGeometry.BarPercent(RangeStart, RangeEnd, created, closed);

        (left + width).ShouldBeLessThanOrEqualTo(100);
        width.ShouldBe(50, 0.001);
    }

    [Fact]
    public void Dado_JanelaDegenerada_Quando_BarPercent_Entao_GeometriaDefensiva()
    {
        var (left, width) = GanttBarGeometry.BarPercent(RangeEnd, RangeEnd, RangeEnd, null);

        left.ShouldBe(0);
        width.ShouldBe(100);
    }

    [Fact]
    public void Dado_IssueFechadaAntesDeCriada_Quando_BarPercent_Entao_LarguraMinimaUmPorCento()
    {
        var created = RangeStart.AddDays(45);
        var closed = created.AddDays(-10);

        var (left, width) = GanttBarGeometry.BarPercent(RangeStart, RangeEnd, created, closed);

        width.ShouldBeGreaterThanOrEqualTo(1);
        (left + width).ShouldBeLessThanOrEqualTo(100);
    }
}
