using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Taskboard.Integrations.Execution;

public sealed class ProcessTreeSignaler : IProcessTreeSignaler, IDisposable
{
    public void Terminate(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Processo não encontrado.
        }
        catch (Exception)
        {
            FallbackKill(processId);
        }
    }

    private static void FallbackKill(int processId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            using var pkill = Process.Start(new ProcessStartInfo("/usr/bin/pkill", $"-TERM -P {processId}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            pkill?.WaitForExit(2000);
        }
        catch
        {
            // Ignora falhas no fallback.
        }
    }

    public void Dispose()
    {
    }
}
