using Shouldly;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class GitCommandRunnerTests
{
    // Covers T2/RF-001: runner captura exit code + stdout
    [Fact]
    public async Task Dado_ComandoValido_Quando_RunAsync_Entao_ExitZeroEStdout()
    {
        var runner = new GitCommandRunner();

        var result = await runner.RunAsync(Directory.GetCurrentDirectory(), ["--version"]);

        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();
        result.StandardOutput.ShouldContain("git version");
    }

    [Fact]
    public async Task Dado_ComandoInvalido_Quando_RunAsync_Entao_ExitNaoZeroEStderrCapturado()
    {
        var runner = new GitCommandRunner();

        var result = await runner.RunAsync(Directory.GetCurrentDirectory(), ["definitivamente-nao-e-um-comando"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotBeNullOrWhiteSpace();
    }

    // Guardrail da SPEC: argumentos via ArgumentList — sem interpolação de shell
    [Fact]
    public async Task Dado_ArgumentoComMetacaracteres_Quando_RunAsync_Entao_NaoExecutaInjection()
    {
        var runner = new GitCommandRunner();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitcmd-{Guid.NewGuid():N}"));

        // "status; touch /tmp/pwned" deve ser tratado como nome de subcomando inválido,
        // nunca como dois comandos — nenhum arquivo pode ser criado.
        var marker = Path.Combine(dir.FullName, "pwned");
        var result = await runner.RunAsync(dir.FullName, [$"status; touch {marker}"]);

        result.ExitCode.ShouldNotBe(0);
        File.Exists(marker).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_EnvHarness_Quando_RunAsync_Entao_VariavelRemovidaDoFilho()
    {
        const string key = "HARNESS_SCRUBBED_TEST";
        Environment.SetEnvironmentVariable(key, "segredo");
        try
        {
            var runner = new GitCommandRunner(executable: "printenv");

            var result = await runner.RunAsync(Directory.GetCurrentDirectory(), [key]);

            result.ExitCode.ShouldBe(1);
            result.StandardOutput.ShouldNotContain("segredo");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task Dado_EnvTaskboard_Quando_RunAsync_Entao_VariavelRemovidaDoFilho()
    {
        const string key = "TASKBOARD_SCRUBBED_TEST";
        Environment.SetEnvironmentVariable(key, "segredo");
        try
        {
            var runner = new GitCommandRunner(executable: "printenv");

            var result = await runner.RunAsync(Directory.GetCurrentDirectory(), [key]);

            // printenv sai 1 quando a variável não existe no ambiente do filho.
            result.ExitCode.ShouldBe(1);
            result.StandardOutput.ShouldNotContain("segredo");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task Dado_ComandoQueExcedeTimeout_Quando_RunAsync_Entao_TimedOut()
    {
        if (!File.Exists("/bin/sleep") && !File.Exists("/usr/bin/sleep"))
        {
            return; // sleep indisponível nesta plataforma
        }

        var runner = new GitCommandRunner(executable: "sleep");

        var result = await runner.RunAsync(
            Directory.GetCurrentDirectory(),
            ["30"],
            timeout: TimeSpan.FromMilliseconds(500));

        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldNotBe(0);
    }

    [Fact]
    public async Task Dado_RepoReal_Quando_StatusPorcelain_Entao_SaidaConsistente()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gitrepo-{Guid.NewGuid():N}"));
        var runner = new GitCommandRunner();

        (await runner.RunAsync(dir.FullName, ["init", "-b", "main"])).ExitCode.ShouldBe(0);
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "novo.txt"), "x");

        var status = await runner.RunAsync(dir.FullName, ["status", "--porcelain"]);

        status.ExitCode.ShouldBe(0);
        status.StandardOutput.ShouldContain("novo.txt");
    }
}
