using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>SPEC-20260929-jobs-dashboard RF-005 — guards de fonte da página /jobs.</summary>
public sealed class JobsPageGuardTests
{
    private static string ReadRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        var path = Path.Join([dir.FullName, .. parts]);
        File.Exists(path).ShouldBeTrue($"arquivo não encontrado: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Dado_JobsRazor_Quando_LeFonte_Entao_TabelaComControlesDeSchedule()
    {
        var source = ReadRepoFile("src", "Taskboard.Blazor", "Components", "Pages", "Jobs.razor");

        source.ShouldContain("@page \"/jobs\"");
        source.ShouldContain("Client.GetJobsAsync");
        source.ShouldContain("Client.UpdateJobAsync");
        source.ShouldContain("Client.RunJobNowAsync");
        source.ShouldContain("form-check form-switch", Case.Sensitive);
    }

    [Fact]
    public void Dado_TaskboardClient_Quando_LeFonte_Entao_MetodosJobsExpostos()
    {
        var source = ReadRepoFile("src", "Taskboard.Blazor", "Services", "TaskboardClient.cs");

        source.ShouldContain("/api/jobs");
        source.ShouldContain("UpdateJobRequest");
    }

    [Fact]
    public void Dado_ProgramCs_Quando_LeFonte_Entao_EndpointsJobsMapeados()
    {
        var source = ReadRepoFile("src", "Taskboard.Server", "Program.cs");

        source.ShouldContain("api.MapGet(\"jobs\"");
        source.ShouldContain("api.MapPut(\"jobs/{key}\"");
        source.ShouldContain("api.MapPost(\"jobs/{key}/run\"");
        source.ShouldContain("JobDefinition");
    }
}
