using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Hooks;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E01/E02/E03 — hook engine: registry
/// resolve por evento+matcher, verdicts allow|warn|block, block_on_failure e a
/// chave Taskboard:Chat:Hooks:Enabled (default on).
/// </summary>
public sealed class ChatHookTests
{
    private sealed class FakeHook(
        string evt,
        string matcher,
        Func<HookContext, CancellationToken, Task<HookVerdict>>? run = null,
        bool blockOnFailure = false,
        string name = "fake") : IChatHook
    {
        public List<HookContext> Seen { get; } = [];
        public string Name => name;
        public string Event => evt;
        public string Matcher => matcher;
        public bool BlockOnFailure => blockOnFailure;

        public Task<HookVerdict> RunAsync(HookContext context, CancellationToken cancellationToken)
        {
            Seen.Add(context);
            return run?.Invoke(context, cancellationToken) ?? Task.FromResult(HookVerdict.Allow);
        }
    }

    private static ChatHookRegistry NewRegistry(
        IEnumerable<IChatHook> hooks, Dictionary<string, string?>? config = null) =>
        new(hooks, new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(),
            NullLogger<ChatHookRegistry>.Instance);

    private static HookContext Ctx(
        string evt = ChatHookEvents.ToolBefore, string subject = "write_file") =>
        new(evt, subject, JsonDocument.Parse("{}").RootElement, WorkspacePath: "/tmp/ws");

    [Fact]
    public async Task Dado_HookDeOutroEvento_Quando_RunAsync_Entao_NaoExecuta()
    {
        var hook = new FakeHook(ChatHookEvents.ToolAfter, ".*");
        var registry = NewRegistry([hook]);

        var result = await registry.RunAsync(Ctx(ChatHookEvents.ToolBefore), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Allow);
        hook.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_MatcherCasaSubject_Quando_RunAsync_Entao_Executa()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, "write_file|edit_file");
        var registry = NewRegistry([hook]);

        await registry.RunAsync(Ctx(subject: "write_file"), CancellationToken.None);
        await registry.RunAsync(Ctx(subject: "read_file"), CancellationToken.None);

        hook.Seen.ShouldHaveSingleItem().Subject.ShouldBe("write_file");
    }

    [Fact]
    public async Task Dado_Warn_Quando_RunAsync_Entao_ColetaVerdict()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, ".*",
            run: (_, _) => Task.FromResult(HookVerdict.Warn("cuidado")));
        var registry = NewRegistry([hook]);

        var result = await registry.RunAsync(Ctx(), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Warn);
        var warn = result.Verdicts.ShouldHaveSingleItem();
        warn.Reason.ShouldBe("cuidado");
        warn.Hook.ShouldBe("fake", "o engine atribui o nome do hook ao verdict");
    }

    [Fact]
    public async Task Dado_Block_Quando_RunAsync_Entao_CurtoCircuito()
    {
        var first = new FakeHook(ChatHookEvents.ToolBefore, ".*",
            run: (_, _) => Task.FromResult(HookVerdict.Block("negado")), name: "first");
        var second = new FakeHook(ChatHookEvents.ToolBefore, ".*", name: "second");
        var registry = NewRegistry([first, second]);

        var result = await registry.RunAsync(Ctx(), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Block);
        result.Block!.Hook.ShouldBe("first");
        second.Seen.ShouldBeEmpty("um block não deixa os hooks seguintes rodarem");
    }

    [Fact]
    public async Task Dado_HookLanca_SemBlockOnFailure_Quando_RunAsync_Entao_FailOpen()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, ".*",
            run: (_, _) => throw new InvalidOperationException("boom"));
        var registry = NewRegistry([hook]);

        var result = await registry.RunAsync(Ctx(), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Allow);
    }

    [Fact]
    public async Task Dado_HookLanca_ComBlockOnFailure_Quando_RunAsync_Entao_FailClosed()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, ".*",
            run: (_, _) => throw new InvalidOperationException("boom"),
            blockOnFailure: true, name: "fragile");
        var registry = NewRegistry([hook]);

        var result = await registry.RunAsync(Ctx(), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Block);
        result.Block!.Hook.ShouldBe("fragile");
        result.Block.Reason!.ShouldContain("boom");
    }

    [Fact]
    public async Task Dado_EngineDesabilitada_Quando_RunAsync_Entao_PulaHooks()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, ".*",
            run: (_, _) => Task.FromResult(HookVerdict.Block("negado")));
        var registry = NewRegistry([hook], new() { [ChatHookRegistry.EnabledKey] = "false" });

        var result = await registry.RunAsync(Ctx(), CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Allow);
        hook.Seen.ShouldBeEmpty();
        registry.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Dado_SemConfig_Quando_Enabled_Entao_DefaultOn()
    {
        NewRegistry([]).Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Dado_MatcherInvalido_Quando_Resolve_Entao_NuncaCasa()
    {
        var hook = new FakeHook(ChatHookEvents.ToolBefore, "[[[");
        var registry = NewRegistry([hook]);

        registry.Resolve(ChatHookEvents.ToolBefore, "write_file").ShouldBeEmpty();
    }

    // ---- config-protection (RF-E03) ----

    private static ConfigProtectionHook NewProtection(string? dataDir = null) =>
        new(SensitivePathDenylist.Shared, dataDir ?? Path.Join(Path.GetTempPath(), "harness-cfg-test"));

    private static HookContext WriteCtx(string argumentsJson, string workspace = "/tmp/ws") =>
        new(ChatHookEvents.ToolBefore, "write_file",
            JsonDocument.Parse(argumentsJson).RootElement, WorkspacePath: workspace);

    [Fact]
    public void Dado_ConfigProtection_Quando_Inspeciona_Entao_ToolBeforeMatcherEscrita()
    {
        var hook = NewProtection();

        hook.Name.ShouldBe("config-protection");
        hook.Event.ShouldBe(ChatHookEvents.ToolBefore);
        hook.Matcher.ShouldBe("write_file|edit_file");
        hook.BlockOnFailure.ShouldBeTrue("proteção de config é fail-closed");
    }

    [Fact]
    public async Task Dado_WriteEmSshRelativo_Quando_ConfigProtection_Entao_BlockCredentialPath()
    {
        var verdict = await NewProtection().RunAsync(
            WriteCtx("""{"path":".ssh/id_rsa"}"""), CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Block);
        verdict.Reason.ShouldBe("credential-path: ssh-dir");
        verdict.Hook.ShouldBe("config-protection");
    }

    [Fact]
    public async Task Dado_EditEmAwsCredentials_Quando_ConfigProtection_Entao_Block()
    {
        var ctx = new HookContext(
            ChatHookEvents.ToolBefore, "edit_file",
            JsonDocument.Parse("""{"path":"/home/u/.aws/credentials"}""").RootElement,
            WorkspacePath: "/tmp/ws");

        var verdict = await NewProtection().RunAsync(ctx, CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Block);
        verdict.Reason.ShouldBe("credential-path: aws-credentials");
    }

    [Fact]
    public async Task Dado_WriteDentroDataDir_Quando_ConfigProtection_Entao_BlockConfigStorage()
    {
        var dataDir = Path.Join(Path.GetTempPath(), $"harness-cfg-{Guid.NewGuid()}");
        var verdict = await NewProtection(dataDir).RunAsync(
            WriteCtx("""{"path":"harness.sqlite"}""", workspace: dataDir), CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Block);
        verdict.Reason.ShouldBe("protected config storage");
    }

    [Fact]
    public async Task Dado_WritePathComum_Quando_ConfigProtection_Entao_Allow()
    {
        var verdict = await NewProtection().RunAsync(
            WriteCtx("""{"path":"src/Foo.cs"}"""), CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Allow);
    }

    [Fact]
    public async Task Dado_ArgsSemPath_Quando_ConfigProtection_Entao_Allow()
    {
        var verdict = await NewProtection().RunAsync(
            WriteCtx("""{"content":"x"}"""), CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Allow);
    }

    // ---- mcp-health-check (RF-E03) ----

    [Fact]
    public void Dado_McpHealthCheck_Quando_Inspeciona_Entao_McpCallWarnOnly()
    {
        var hook = new McpHealthCheckHook();

        hook.Name.ShouldBe("mcp-health-check");
        hook.Event.ShouldBe(ChatHookEvents.McpCall);
        hook.Matcher.ShouldBe("mcp_*");
        ((IChatHook)hook).BlockOnFailure.ShouldBeFalse("health check nunca bloqueia");
    }

    [Fact]
    public async Task Dado_ServerDegradado_Quando_McpCall_Entao_Warn()
    {
        var hook = new McpHealthCheckHook();
        var ctx = new HookContext(
            ChatHookEvents.McpCall, "mcp_fake_echo",
            Meta: new Dictionary<string, string>
            {
                ["server"] = "fake",
                ["healthy"] = "false",
                ["error"] = "connect failed",
            });

        var verdict = await hook.RunAsync(ctx, CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Warn);
        verdict.Reason!.ShouldContain("fake");
        verdict.Hook.ShouldBe("mcp-health-check");
    }

    [Fact]
    public async Task Dado_ServerSaudavel_Quando_McpCall_Entao_Allow()
    {
        var hook = new McpHealthCheckHook();
        var ctx = new HookContext(
            ChatHookEvents.McpCall, "mcp_fake_echo",
            Meta: new Dictionary<string, string>
            {
                ["server"] = "fake",
                ["healthy"] = "true",
            });

        (await hook.RunAsync(ctx, CancellationToken.None)).Kind.ShouldBe(HookVerdictKind.Allow);
    }

    [Fact]
    public async Task Dado_SemMeta_Quando_McpCall_Entao_Allow()
    {
        var hook = new McpHealthCheckHook();

        var verdict = await hook.RunAsync(
            new HookContext(ChatHookEvents.McpCall, "mcp_x_y"), CancellationToken.None);

        verdict.Kind.ShouldBe(HookVerdictKind.Allow);
    }
}
