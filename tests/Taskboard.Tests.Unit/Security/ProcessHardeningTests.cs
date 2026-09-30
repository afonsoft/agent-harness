using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Security;

/// <summary>
/// SPEC-20260930-sonar-s6471 + SPEC-20260930-sonar-s4036:
/// - Dockerfile runtime não pode executar como root (docker:S6471).
/// - Comandos de processo devem usar caminho absoluto, sem depender de
///   resolução via PATH (csharpsquid:S4036).
/// </summary>
public class ProcessHardeningTests
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

    [Fact]
    public void Dado_Dockerfile_Quando_LeFonte_Entao_EntrypointNaoRoot()
    {
        var src = RepoFile("Dockerfile");

        var userIdx = src.LastIndexOf("\nUSER ", StringComparison.Ordinal);
        var entryIdx = src.LastIndexOf("ENTRYPOINT", StringComparison.Ordinal);

        userIdx.ShouldBeGreaterThan(0, "Dockerfile deve declarar USER antes do ENTRYPOINT");
        entryIdx.ShouldBeGreaterThan(userIdx, "USER deve vir antes do ENTRYPOINT");
        var userLine = src[(userIdx + 1)..src.IndexOf('\n', userIdx + 1)].Trim();
        userLine.ShouldNotBe("USER root", "runtime não pode rodar como root");
    }

    [Fact]
    public void Dado_ProcessTreeSignaler_Quando_LeFonte_Entao_PkillAbsoluto()
    {
        var src = RepoFile("src/Taskboard.Integrations/Execution/ProcessTreeSignaler.cs");

        src.Contains("""new ProcessStartInfo("pkill")""", StringComparison.Ordinal)
            .ShouldBeFalse("pkill deve ser invocado por caminho absoluto (S4036)");
        System.Text.RegularExpressions.Regex.IsMatch(
                src, @"ProcessStartInfo\(""/(usr/)?bin/|ProcessStartInfo\(""/usr/")
            .ShouldBeTrue("esperado caminho absoluto para o comando de kill");
    }
}
