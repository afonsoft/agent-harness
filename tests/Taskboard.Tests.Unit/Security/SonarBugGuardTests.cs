using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Security;

/// <summary>
/// SPEC-20260930-sonar-s2259 / javascript-S4822: contratos de fonte para as
/// correções de bug remanescentes do SonarCloud que vivem em caminhos de
/// código que o teste comportamental não distingue (guard de null explícito)
/// ou em JS puro (await de promise dentro de try).
/// </summary>
public class SonarBugGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return dir.FullName;
    }

    private static string SourceOf(params string[] parts)
    {
        var path = Path.Join([RepoRoot(), .. parts]);
        File.Exists(path).ShouldBeTrue($"arquivo esperado não encontrado: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Dado_LoopDeEstado_Quando_StateNull_Entao_GuardExplicitoAntesDeDeref()
    {
        // SPEC-20260930-sonar-s2259: 'state' pode ser null; o guard `state is null`
        // deve short-circuit antes de qualquer deref de FileModifiedUtc/FileSizeBytes.
        var src = SourceOf(
            "src", "Taskboard.Application", "CliMetrics", "CliMetricsService.cs");

        var loopStart = src.IndexOf("foreach (var (source, joined, mtime, size) in signatures)", StringComparison.Ordinal);
        loopStart.ShouldBeGreaterThan(0, "loop de assinaturas não encontrado");
        var loopEnd = src.IndexOf("unchanged = false;", loopStart, StringComparison.Ordinal);
        loopEnd.ShouldBeGreaterThan(loopStart, "marcação de unchanged não encontrada no loop");
        var region = src[loopStart..loopEnd];

        region.ShouldContain("state is null",
            customMessage: "o loop deve testar `state is null` explicitamente antes de dereferenciar");
    }

    [Fact]
    public void Dado_EnsurePermission_Quando_RequestPermission_Entao_AwaitDentroDoTry()
    {
        // javascript:S4822 — promise retornada dentro de try sem await escapa
        // do catch; ensurePermission deve ser async e awaitar requestPermission().
        var js = SourceOf(
            "src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");

        Regex.IsMatch(js, @"ensurePermission:\s*async\s+function")
            .ShouldBeTrue("ensurePermission deve ser async");
        Regex.IsMatch(js, @"return\s+await\s+Notification\.requestPermission\(\)")
            .ShouldBeTrue("requestPermission() deve ser awaitado dentro do try");
    }
}
