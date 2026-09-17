using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Integrations.Terminal;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Terminal;

/// <summary>
/// Real-PTY smoke tests for <see cref="PtySession"/> (SPEC-20260917-cli-agents-terminal).
/// Require the <c>script</c> binary (bsdutils) — skipped when unavailable.
/// </summary>
public class PtySessionTests : IDisposable
{
    private static readonly string? ScriptPath = new[] { "/usr/bin/script", "/bin/script" }
        .FirstOrDefault(File.Exists);

    private readonly string _homeDir;

    public PtySessionTests()
    {
        _homeDir = Path.Join(Path.GetTempPath(), $"tb-pty-{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_homeDir, recursive: true);
        }
        catch
        {
            // Temp cleanup best-effort.
        }
    }

    private PtySession CreateSession() =>
        new(_homeDir, NullLogger.Instance, executableLocator: _ => ScriptPath);

    [Fact]
    public async Task Dado_SessaoIniciada_Quando_EscreveComando_Entao_RecebeSaida()
    {
        if (ScriptPath is null)
        {
            return; // script (bsdutils) not installed on this host.
        }

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = CreateSession();
        session.OutputReceived += chunk => output.Append(chunk);
        session.Exited += code => exited.TrySetResult(code);

        session.Start();
        session.IsRunning.ShouldBeTrue();

        var marker = $"tb-pty-{Guid.NewGuid():N}";
        await session.WriteAsync($"echo {marker}\n");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!output.ToString().Contains(marker, StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        output.ToString().ShouldContain(marker);

        await session.WriteAsync("exit\n");
        var completed = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.ShouldBe(exited.Task, "o shell deve encerrar após 'exit'");
    }

    [Fact]
    public async Task Dado_SessaoDescartada_Quando_DisposeAsync_Entao_ProcessoFinalizado()
    {
        if (ScriptPath is null)
        {
            return;
        }

        var session = CreateSession();
        session.Start();
        session.IsRunning.ShouldBeTrue();

        await session.DisposeAsync();

        session.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ScriptAusente_Quando_Start_Entao_LancaInvalidOperation()
    {
        await using var session = new PtySession(
            _homeDir, NullLogger.Instance, executableLocator: _ => null);

        Should.Throw<InvalidOperationException>(session.Start);
    }

    [Fact]
    public async Task Dado_Input_Quando_WriteAsync_Entao_AtualizaLastActivity()
    {
        if (ScriptPath is null)
        {
            return;
        }

        await using var session = CreateSession();
        session.Start();
        var before = session.LastActivityUtc;

        await Task.Delay(20);
        await session.WriteAsync("true\n");

        session.LastActivityUtc.ShouldBeGreaterThan(before);
    }
}
