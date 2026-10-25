using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261025 C2: docker sandbox — chat exec tools run inside a
/// short-lived container: the workspace bind-mounts at the same path
/// read-write, host <c>$HOME</c>/<c>~/.agent-harness</c> are not mounted,
/// no network, and cgroup caps bound the blast radius. Config:
/// <c>Taskboard:Chat:Sandbox</c> = <c>off|docker</c> (default off),
/// <c>Taskboard:Chat:SandboxImage</c> overrides the base image.
/// Availability is probed via <c>docker info</c> and cached (TTL) — when the
/// socket is down the caller falls back to host exec with a warning.
/// </summary>
public sealed class DockerChatSandbox : IChatToolSandbox
{
    public const string ModeKey = "Taskboard:Chat:Sandbox";
    public const string ImageKey = "Taskboard:Chat:SandboxImage";
    public const string NetworkKey = "Taskboard:Chat:SandboxNetwork";
    public const string DefaultImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    internal static readonly TimeSpan DefaultProbeTtl = TimeSpan.FromSeconds(60);

    private readonly string _image;
    private readonly bool _network;
    private readonly TimeSpan _probeTtl;
    private readonly Func<CancellationToken, Task<bool>> _probe;
    private bool? _available;
    private DateTimeOffset _availableAt;

    public DockerChatSandbox(
        IConfiguration configuration,
        Func<CancellationToken, Task<bool>>? probe = null,
        TimeSpan? probeTtl = null)
    {
        Enabled = string.Equals(configuration[ModeKey], "docker", StringComparison.OrdinalIgnoreCase);
        _image = configuration[ImageKey] is { Length: > 0 } image ? image : DefaultImage;
        _network = bool.TryParse(configuration[NetworkKey], out var net) && net;
        _probeTtl = probeTtl ?? DefaultProbeTtl;
        _probe = probe ?? ProbeSocketAsync;
    }

    public string Name => $"docker ({_image})";

    public bool Enabled { get; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return false;
        }

        if (_available is { } cached && DateTimeOffset.UtcNow - _availableAt < _probeTtl)
        {
            return cached;
        }

        var available = await _probe(cancellationToken).ConfigureAwait(false);
        _available = available;
        _availableAt = DateTimeOffset.UtcNow;
        return available;
    }

    public void Wrap(ProcessStartInfo startInfo)
    {
        var workspace = startInfo.WorkingDirectory;
        var innerArgs = new List<string> { startInfo.FileName };
        innerArgs.AddRange(startInfo.ArgumentList);

        startInfo.ArgumentList.Clear();
        foreach (var arg in new[]
        {
            "run", "--rm", "-i",
            "--network", _network ? "bridge" : "none",
            "--memory", "1g", "--cpus", "2", "--pids-limit", "256",
            // Host HOME is not mounted; give the process a container-local one.
            "--env", "HOME=/tmp",
            "--volume", $"{workspace}:{workspace}",
            "--workdir", workspace,
            _image,
        })
        {
            startInfo.ArgumentList.Add(arg);
        }
        foreach (var arg in innerArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }
        startInfo.FileName = "docker";
        // WorkingDirectory stays — it is the docker client's cwd, and the
        // container gets its own -w for the inner process.
    }

    /// <summary><c>docker info</c> reachability — every failure mode maps to unavailable.</summary>
    private static async Task<bool> ProbeSocketAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("info");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.ServerVersion}}");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
