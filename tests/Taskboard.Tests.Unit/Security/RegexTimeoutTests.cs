using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Security;

/// <summary>
/// SPEC-20260930-sonar-s6444 RF-001: todo uso de Regex (campo estático ou
/// chamada IsMatch/Replace) deve declarar um matchTimeout (TimeSpan) para
/// limitar o tempo de execução — sem timeout, padrões adversariais permitem
/// ReDoS. SonarQube rule csharpsquid:S6444.
/// </summary>
public class RegexTimeoutTests
{
    private static readonly string[] Files =
    [
        "src/Taskboard.Domain/Entities/AgentCliDefinition.cs",
        "src/Taskboard.Integrations/Agents/DockerCliSpawner.cs",
        "src/Taskboard.Integrations/Agents/AgentCliInstallService.cs",
        "src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs",
        "src/Taskboard.Integrations/Skills/SkillsRepository.cs",
        "src/Taskboard.Integrations/Vscode/VscodeInstallService.cs",
        "src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs",
        "src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs",
        "src/Taskboard.Blazor/Services/RepositoryFilter.cs",
        "src/Taskboard.Server/Program.cs",
    ];

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
    public void Dado_ArquivosComRegex_Quando_LeFonte_Entao_TodoRegexTemTimeout()
    {
        foreach (var rel in Files)
        {
            var src = RepoFile(rel);
            // Nomes de campos TimeSpan declarados no arquivo (ex.: RegexTimeout)
            // contam como timeout — o valor TimeSpan.FromSeconds vive na declaração.
            var timeoutNames = System.Text.RegularExpressions.Regex.Matches(
                    src, @"\bTimeSpan\s+(\w+)\s*=")
                .Select(m => m.Groups[1].Value)
                .ToList();
            // Remove literais (verbatim, com escape, char) — o próprio padrão
            // regex pode conter ';' e quebrar o split de statements.
            var sanitized = System.Text.RegularExpressions.Regex.Replace(
                src, """@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])'""", "\"\"");
            // Split em statements ';' — field initializers multi-linha contam como um.
            var statements = sanitized.Split(';');
            var regexSites = statements
                .Where(s =>
                    System.Text.RegularExpressions.Regex.IsMatch(s, @"\bRegex\s+\w+\s*=\s*new\s*\(") ||
                    System.Text.RegularExpressions.Regex.IsMatch(s, @"\bRegex\.(IsMatch|Replace|Match|Matches|Split)\s*\("))
                .ToList();

            foreach (var site in regexSites)
            {
                (site.Contains("TimeSpan") || site.Contains("matchTimeout") ||
                 timeoutNames.Any(site.Contains))
                    .ShouldBeTrue($"regex sem matchTimeout em {rel}: {site.Trim()[..Math.Min(80, site.Trim().Length)]}...");
            }
        }
    }
}
