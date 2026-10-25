using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261025 C2 — docker sandbox routing + host fallback.</summary>
public sealed class ChatSandboxTests
{
    private static IConfiguration Cfg(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void Dado_ModoOff_Quando_Construido_Entao_Disabled()
    {
        var sandbox = new DockerChatSandbox(Cfg());
        sandbox.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Dado_ModoDocker_Quando_Construido_Entao_EnabledEImagemDefault()
    {
        var sandbox = new DockerChatSandbox(Cfg((DockerChatSandbox.ModeKey, "docker")));
        sandbox.Enabled.ShouldBeTrue();
        sandbox.Name.ShouldContain(DockerChatSandbox.DefaultImage);
    }

    [Fact]
    public void Dado_Wrap_Quando_Aplicado_Entao_DockerRunArgsComMountENoNetwork()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "ws-sbx");
        var sandbox = new DockerChatSandbox(Cfg((DockerChatSandbox.ModeKey, "docker")));
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workspace,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("echo hi");

        sandbox.Wrap(startInfo);

        startInfo.FileName.ShouldBe("docker");
        var args = startInfo.ArgumentList.ToList();
        args[0].ShouldBe("run");
        args.ShouldContain("--rm");
        args.ShouldContain("none");
        args.ShouldContain("--memory");
        args.ShouldContain("--pids-limit");
        args.ShouldContain($"{workspace}:{workspace}");
        args.ShouldContain(workspace);
        args.ShouldContain(DockerChatSandbox.DefaultImage);
        // Inner command preserved verbatim after the image.
        args[^3].ShouldBe("/bin/sh");
        args[^2].ShouldBe("-c");
        args[^1].ShouldBe("echo hi");
    }

    [Fact]
    public async Task Dado_ProbeFalha_Quando_IsAvailable_Entao_FalsoECacheado()
    {
        var probes = 0;
        var sandbox = new DockerChatSandbox(
            Cfg((DockerChatSandbox.ModeKey, "docker")),
            probe: _ => { probes++; return Task.FromResult(false); });

        (await sandbox.IsAvailableAsync()).ShouldBeFalse();
        (await sandbox.IsAvailableAsync()).ShouldBeFalse();
        probes.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SandboxDisponivel_Quando_RunAsync_Entao_RoteiaPeloWrap()
    {
        var sandbox = new FakeSandbox(available: true);
        var result = await ChatProcessRunner.RunAsync(
            "/bin/sh", ["-c", "echo host"], Path.GetTempPath(),
            TimeSpan.FromSeconds(10), CancellationToken.None, sandbox: sandbox);

        // The fake wraps every exec into `echo SANDBOXED` — if stdout carries
        // the marker the argv was rewritten through the sandbox path.
        result.Stdout.Trim().ShouldBe("SANDBOXED");
        result.SandboxWarning.ShouldBeNull();
        sandbox.WrapCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SandboxIndisponivel_Quando_RunAsync_Entao_HostFallbackComWarning()
    {
        var sandbox = new FakeSandbox(available: false);
        var result = await ChatProcessRunner.RunAsync(
            "/bin/sh", ["-c", "echo host"], Path.GetTempPath(),
            TimeSpan.FromSeconds(10), CancellationToken.None, sandbox: sandbox);

        result.Stdout.Trim().ShouldBe("host");
        var warning = result.SandboxWarning!;
        warning.ShouldContain("unavailable");
        warning.ShouldContain("host");
        sandbox.WrapCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_SandboxDesligado_Quando_RunAsync_Entao_ExecutaNoHostSemWarning()
    {
        var sandbox = new DockerChatSandbox(
            Cfg(), probe: _ => Task.FromResult(true));
        var result = await ChatProcessRunner.RunAsync(
            "/bin/sh", ["-c", "echo host"], Path.GetTempPath(),
            TimeSpan.FromSeconds(10), CancellationToken.None, sandbox: sandbox);

        result.Stdout.Trim().ShouldBe("host");
        result.SandboxWarning.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ShellExec_Quando_SandboxIndisponivel_Entao_WarningNoResultado()
    {
        var tool = new ShellExecTool(
            new Taskboard.Integrations.Harness.Security.DynamicCommandClassifier(),
            new Taskboard.Integrations.Harness.Security.SecretScrubber(),
            sandbox: new FakeSandbox(available: false));
        var args = JsonDocument.Parse("""{"command":"echo hi"}""").RootElement;
        var context = new ChatToolContext(
            WorkspacePath: Path.GetTempPath(), ProviderId: Guid.NewGuid(),
            ProviderBaseUrl: "http://x", ProviderApiKey: "k", ImageModel: "",
            SearchBackend: "none", SearchUrl: "", SearchApiKey: "");

        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        result.Json.ShouldContain("sandboxWarning");
        result.Json.ShouldContain("host");
    }

    private sealed class FakeSandbox(bool available) : IChatToolSandbox
    {
        public string Name => "fake-sandbox";
        public bool Enabled => true;
        public int WrapCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available);

        public void Wrap(ProcessStartInfo startInfo)
        {
            WrapCalls++;
            startInfo.ArgumentList.Clear();
            startInfo.ArgumentList.Add("SANDBOXED");
            startInfo.FileName = "/bin/echo";
        }
    }
}
