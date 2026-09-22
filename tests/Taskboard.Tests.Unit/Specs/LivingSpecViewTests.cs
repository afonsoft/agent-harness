using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>
/// SPEC-20260922-living-specs-default-view RF-001/RF-003 — partition rule for
/// the /specs default view: all non-Done (Deprecated included) then the latest
/// N Done, preserving the endpoint order inside each group.
/// </summary>
public class LivingSpecViewTests
{
    private static LivingSpecDto Spec(string id, string status) => new(
        Id: id,
        Title: id,
        Type: "Feature",
        Status: status,
        RawStatus: status,
        Date: "2026-09-22",
        Ticket: null,
        RequirementsCount: 0,
        AcceptanceCriteriaCount: 0,
        TasksTotal: 0,
        TasksDone: 0,
        Warnings: []);

    [Fact]
    public void Dado_ListaMista_Quando_Particiona_Entao_NaoDonePrimeiroDoneCap()
    {
        var specs = new List<LivingSpecDto>
        {
            Spec("S-090", "Done"),
            Spec("S-089", "Draft"),
            Spec("S-088", "Done"),
            Spec("S-087", "Approved"),
            Spec("S-086", "Done"),
            Spec("S-085", "Done"),
            Spec("S-084", "Done"),
            Spec("S-083", "Done"),
            Spec("S-082", "Done"),
            Spec("S-081", "Done"),
            Spec("S-080", "Done"),
            Spec("S-079", "Done"),
            Spec("S-078", "Done"),
            Spec("S-077", "Deprecated"),
        };

        var partition = LivingSpecView.Partition(specs, doneCap: 10);

        // 3 non-Done (Draft, Approved, Deprecated) + 10 newest Done
        partition.Visible.Count.ShouldBe(13);
        partition.DoneCount.ShouldBe(11);
        partition.HiddenDoneCount.ShouldBe(1);

        partition.Visible[0].Id.ShouldBe("S-089");
        partition.Visible[1].Id.ShouldBe("S-087");
        partition.Visible[2].Id.ShouldBe("S-077");
        partition.Visible[3].Id.ShouldBe("S-090");
        partition.Visible[12].Id.ShouldBe("S-079");
        partition.Visible.ShouldNotContain(s => s.Id == "S-078");
    }

    [Fact]
    public void Dado_MenosDe10Done_Quando_Particiona_Entao_HiddenZero()
    {
        var specs = new List<LivingSpecDto>
        {
            Spec("S-010", "Done"),
            Spec("S-009", "InImplementation"),
            Spec("S-008", "Done"),
        };

        var partition = LivingSpecView.Partition(specs, LivingSpecView.DoneCap);

        partition.Visible.Count.ShouldBe(3);
        partition.HiddenDoneCount.ShouldBe(0);
        partition.DoneCount.ShouldBe(2);
        partition.Visible[^1].Id.ShouldBe("S-008");
    }

    [Fact]
    public void Dado_Deprecated_Quando_Particiona_Entao_SempreVisivel()
    {
        var specs = Enumerable.Range(1, 15)
            .Select(i => Spec($"S-{i:D3}", i == 8 ? "Deprecated" : "Done"))
            .ToList();

        var partition = LivingSpecView.Partition(specs, LivingSpecView.DoneCap);

        partition.Visible[0].Status.ShouldBe("Deprecated");
        partition.Visible.Count.ShouldBe(11);
        partition.HiddenDoneCount.ShouldBe(4);
    }

    [Fact]
    public void Dado_CapZero_Quando_Particiona_Entao_NenhumDone()
    {
        var specs = new List<LivingSpecDto>
        {
            Spec("S-005", "Done"),
            Spec("S-004", "Draft"),
            Spec("S-003", "Done"),
        };

        var partition = LivingSpecView.Partition(specs, doneCap: 0);

        partition.Visible.ShouldBe([specs[1]]);
        partition.HiddenDoneCount.ShouldBe(2);
    }

    [Fact]
    public void Dado_StatusMinusculo_Quando_Particiona_Entao_ContaComoDone()
    {
        var specs = new List<LivingSpecDto>
        {
            Spec("S-002", "done"),
            Spec("S-001", "Draft"),
        };

        var partition = LivingSpecView.Partition(specs, doneCap: 0);

        partition.DoneCount.ShouldBe(1);
        partition.Visible.ShouldBe([specs[1]]);
    }

    [Fact]
    public void Dado_ListaVazia_Quando_Particiona_Entao_ResultadoVazio()
    {
        var partition = LivingSpecView.Partition([], LivingSpecView.DoneCap);

        partition.Visible.ShouldBeEmpty();
        partition.HiddenDoneCount.ShouldBe(0);
        partition.DoneCount.ShouldBe(0);
    }
}
