using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Terminal;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Terminal;

/// <summary>
/// Real-PTY tests for <see cref="PtySession.ResizeAsync"/>
/// (SPEC-20260920-terminal-pty-resize): kernel-level TIOCSWINSZ must reach
/// the shell without ever echoing an "stty" command into the terminal.
/// Linux-only — the harness is gated on OperatingSystem.IsLinux().
/// </summary>
public class PtySessionResizeTests
{
    private sealed class PtyHarness : IAsyncDisposable
    {
        public PtySession Session { get; }
        public StringBuilder Output { get; } = new();
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PtyHarness()
        {
            Session = new PtySession(
                Path.GetTempPath(),
                NullLogger.Instance,
                cols: 120, rows: 30);
            Session.OutputReceived += chunk => Output.Append(chunk);
            Session.Exited += _ => _exited.TrySetResult();
            Session.Start();
        }

        public async Task WaitForOutputAsync(string needle, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (Output.ToString().Contains(needle, StringComparison.Ordinal))
                {
                    return;
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"PTY output did not contain '{needle}'. Got: {Output}");
        }

        public async ValueTask DisposeAsync() => await Session.DisposeAsync();
    }

    [Fact]
    public async Task Dado_PtyReal_Quando_Resize_Entao_IoctlSemTextoStty()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var h = new PtyHarness();
        await h.Session.WriteAsync("echo READY-$((1+1))\n");
        await h.WaitForOutputAsync("READY-2");

        h.Output.Clear();
        await h.Session.ResizeAsync(163, 41);

        // O shell deve ver o novo winsize via stty size (rows cols).
        await h.Session.WriteAsync("stty size\n");
        await h.WaitForOutputAsync("41 163");

        // Nenhum "stty cols … rows …" injetado/ecoado — a única aparição de
        // "stty" permitida é a digitada pelo próprio teste ("stty size").
        h.Output.ToString().ShouldNotContain("stty cols");
        h.Output.ToString().ShouldNotContain("rows 41");
    }

    [Fact]
    public async Task Dado_MesmaDimensao_Quando_ResizeRepetido_Entao_Deduplicado()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var h = new PtyHarness();
        await h.Session.WriteAsync("echo READY-$((2+2))\n");
        await h.WaitForOutputAsync("READY-4");

        h.Output.Clear();
        await h.Session.ResizeAsync(200, 50);
        await h.Session.ResizeAsync(200, 50);
        await h.Session.ResizeAsync(200, 50);
        await h.Session.WriteAsync("stty size\n");
        await h.WaitForOutputAsync("50 200");

        h.Output.ToString().ShouldNotContain("stty cols");
    }

    [Fact]
    public async Task Dado_DimensaoInvalida_Quando_Resize_Entao_Ignorada()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using var h = new PtyHarness();
        await h.Session.WriteAsync("echo READY-$((3+3))\n");
        await h.WaitForOutputAsync("READY-6");

        h.Output.Clear();
        await h.Session.ResizeAsync(0, 0);
        await h.Session.ResizeAsync(-5, 3);
        await h.Session.ResizeAsync(10_000, 10_000);
        await h.Session.WriteAsync("stty size\n");
        await h.WaitForOutputAsync("30 120"); // winsize inicial preservado

        h.Output.ToString().ShouldNotContain("stty cols");
    }
}
