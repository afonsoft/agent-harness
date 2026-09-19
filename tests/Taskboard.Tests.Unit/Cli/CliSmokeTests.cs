using Shouldly;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
using Taskboard.Cli;
using Taskboard.Cli.Services;
using Xunit;
using CliProgram = Taskboard.Cli.Program;

namespace Taskboard.Tests.Unit.Cli;

public class CliSmokeTests
{
    private static CommandAppTester CriarTester()
    {
        var tester = new CommandAppTester();
        tester.Configure(CliProgram.ConfigureCommands);
        return tester;
    }

    [Fact]
    public async Task Dado_HelpRaiz_Quando_Executado_Entao_ListaTodosOsComandos()
    {
        var tester = CriarTester();

        var result = await tester.RunAsync(["--help"]);

        result.ExitCode.ShouldBe(0);
        foreach (var comando in ComandosRegistrados.Select(c => (string)c[0]))
        {
            result.Output.ShouldContain(comando);
        }
    }

    [Theory]
    [MemberData(nameof(ComandosRegistrados))]
    public async Task Dado_HelpDeComando_Quando_Executado_Entao_NaoQuebraParser(string comando)
    {
        var tester = CriarTester();

        var result = await tester.RunAsync([comando, "--help"]);

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_ContextCurrentOffline_Quando_Executado_Entao_RetornaZero()
    {
        var tester = CriarTester();

        var result = await tester.RunAsync(["context:current"]);

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Dado_CommandArgument_Quando_Registrado_Entao_PlaceholderUsaColchetes()
    {
        // Regressão do gotcha Spectre: nome cru ("project") vira markup [project]
        // e quebra o --help inteiro. Convenção: "<nome>" ou "[nome]".
        var assembly = typeof(CliProgram).Assembly;
        var violacoes = new List<string>();

        foreach (var tipo in assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(CommandSettings))))
        {
            foreach (var prop in tipo.GetProperties())
            {
                var attr = prop.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType == typeof(CommandArgumentAttribute));
                if (attr is null || attr.ConstructorArguments.Count < 2)
                {
                    continue;
                }

                var nome = attr.ConstructorArguments[1].Value as string;
                if (nome is null || !(nome.StartsWith('<') || nome.StartsWith('[')))
                {
                    violacoes.Add($"{tipo.Name}.{prop.Name} = \"{nome}\"");
                }
            }
        }

        violacoes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("owner/repo", "owner", "repo")]
    [InlineData("my-org/my.repo", "my-org", "my.repo")]
    public void Dado_RepositorioValido_Quando_SplitRepo_Entao_RetornaOwnerENome(string input, string owner, string name)
    {
        var (actualOwner, actualName) = GitHubIssueCommentListCommand.SplitRepo(input);

        actualOwner.ShouldBe(owner);
        actualName.ShouldBe(name);
    }

    [Theory]
    [InlineData("sem-barra")]
    [InlineData("a/b/c")]
    [InlineData("")]
    public void Dado_RepositorioInvalido_Quando_SplitRepo_Entao_LancaCliException(string input)
    {
        var ex = Should.Throw<CliException>(() => GitHubIssueCommentListCommand.SplitRepo(input));

        ex.ExitCode.ShouldBe(2);
    }

    public static IEnumerable<object[]> ComandosRegistrados
        => new List<object[]>
        {
            new object[] { "context:current" },
            new object[] { "ghissue:history" },
            new object[] { "ghissue:comments" },
            new object[] { "ghissue:comment" },
            new object[] { "cloud:login" },
            new object[] { "cloud:status" },
            new object[] { "cloud:logout" },
        };
}
