using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Security;

/// <summary>
/// SPEC-20260930-sonar-s2930 RF-001: CancellationTokenSource fields must be
/// disposed, not only cancelled — S2930 "Dispose '_cts' when it is no longer
/// needed" (BLOCKER). Contrato de fonte: cada arquivo que cancela o campo
/// também precisa descartá-lo.
/// </summary>
public class CancellationSourceDisposeTests
{
    private static string RepoFile(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        var path = Path.Join(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue($"arquivo não encontrado: {path}");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor", "_cts")]
    [InlineData("src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor", "_cts")]
    [InlineData("src/Taskboard.Blazor/Components/Pages/Settings.razor", "_pollCts")]
    [InlineData("src/Taskboard.Integrations/Terminal/PtySession.cs", "_pumpCts")]
    public void Dado_CampoCts_Quando_LeFonte_Entao_CancelEDispose(string rel, string field)
    {
        var src = RepoFile(rel);

        src.Contains($"{field}?.Cancel", StringComparison.Ordinal)
            .ShouldBeTrue($"{rel}: {field} deve ser cancelado");
        System.Text.RegularExpressions.Regex.IsMatch(
                src, $@"{System.Text.RegularExpressions.Regex.Escape(field)}\?\.(Dispose|DisposeAsync)\b")
            .ShouldBeTrue($"{rel}: {field} deve ser descartado (S2930)");
    }
}
