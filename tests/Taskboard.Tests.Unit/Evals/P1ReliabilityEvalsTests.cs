using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Integrations.Chat.Hooks;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Evals;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-H01: offline evals — deterministic
/// provider/tool fakes, zero live network. Each eval replays a P0/P1
/// reliability invariant end-to-end so a regression fails here before it
/// fails in production. Seed rows live in the root REGRESSION.md.
/// </summary>
public class P1ReliabilityEvalsTests
{
    /// <summary>
    /// Deferred tools: discover → <c>tool_search</c> → <c>tool_activate</c> →
    /// the promoted tool is active on the next provider call, and the
    /// promotion ledger is append-only (re-activation never duplicates).
    /// </summary>
    [Fact]
    public async Task Eval_DeferredTools_DiscoverActivate_PromotesForNextPlan()
    {
        var catalog = new DeferredToolCatalog(new ConfigurationBuilder().Build());
        var ctx = new ChatToolContext(
            "/tmp/eval-ws", Guid.NewGuid(), "http://fake.local", "k",
            "img", "none", "", "");
        var toolSet = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["tool_search"] = FakeTool("tool_search"),
            ["tool_activate"] = FakeTool("tool_activate"),
            ["echo_tool"] = FakeTool("echo_tool"),
            ["browser_use"] = FakeTool("browser_use"),
        };

        var plan1 = await catalog.PlanAsync("conv-eval", toolSet, ctx, CancellationToken.None);
        plan1.ActiveNames.ShouldContain("echo_tool");
        plan1.ActiveNames.ShouldContain("tool_search");
        plan1.ActiveNames.ShouldNotContain("browser_use");
        plan1.Deferred.Select(d => d.Name).ShouldContain("browser_use");

        var hit = catalog.Search("conv-eval", "browser").ShouldHaveSingleItem();
        hit.Name.ShouldBe("browser_use");
        hit.Available.ShouldBeTrue();

        catalog.Activate("conv-eval", "browser_use").Ok.ShouldBeTrue();
        catalog.PromotedNames("conv-eval").ShouldBe(["browser_use"]);

        // Append-only: a second activation is a no-op on the ledger.
        catalog.Activate("conv-eval", "browser_use").Ok.ShouldBeTrue();
        catalog.PromotedNames("conv-eval").Count(n => n == "browser_use").ShouldBe(1);

        var plan2 = await catalog.PlanAsync("conv-eval", toolSet, ctx, CancellationToken.None);
        // The wire boundary merges active ∪ promoted — this is what the next
        // provider call actually receives.
        var wire = DeferredToolWire.BuildDefinitions(
            toolSet, plan2, catalog.PromotedNames("conv-eval"));
        wire.Select(d => d.Name).ShouldContain("browser_use");
    }

    /// <summary>
    /// Credential-path refusal: <c>tool.before</c> on a write into
    /// <c>~/.ssh</c> folds to a block verdict carrying the credential class —
    /// the tool never executes and the raw path never leaks into the reason.
    /// </summary>
    [Fact]
    public async Task Eval_CredentialPath_WriteToSsh_BlocksBeforeExecution()
    {
        var registry = new ChatHookRegistry(
            [new ConfigProtectionHook(new SensitivePathDenylist(), "/tmp/eval-config")],
            new ConfigurationBuilder().Build());
        var args = JsonDocument.Parse(
            """{"path":"/home/u/.ssh/id_rsa","content":"x"}""").RootElement;

        var result = await registry.RunAsync(
            new HookContext(ChatHookEvents.ToolBefore, "write_file", args,
                WorkspacePath: "/tmp/eval-ws"),
            CancellationToken.None);

        result.Kind.ShouldBe(HookVerdictKind.Block);
        var block = result.Block.ShouldNotBeNull();
        block.Reason.ShouldNotBeNull().ShouldContain("credential-path");
        block.Reason.ShouldNotContain("id_rsa");
    }

    /// <summary>
    /// Preflight verdict mapping: check statuses fold to the aggregate
    /// verdict — blocked beats warning, warning beats ready.
    /// </summary>
    [Fact]
    public void Eval_Preflight_VerdictMapping_BlockedBeatsWarningBeatsReady()
    {
        PreflightReport.VerdictOf(
            [Check("cli", PreflightCheckStatus.Blocked)]).ShouldBe("blocked");
        PreflightReport.VerdictOf(
            [Check("cli", PreflightCheckStatus.Ok),
             Check("mcp", PreflightCheckStatus.Warning)]).ShouldBe("warning");
        PreflightReport.VerdictOf(
            [Check("cli", PreflightCheckStatus.Ok),
             Check("disk", PreflightCheckStatus.Ok)]).ShouldBe("ready");
        PreflightReport.VerdictOf(
            [Check("mcp", PreflightCheckStatus.Warning),
             Check("cli", PreflightCheckStatus.Blocked)]).ShouldBe("blocked");
    }

    private static PreflightCheck Check(string name, PreflightCheckStatus status) =>
        new(name, status, "eval");

    private static IChatTool FakeTool(string name)
    {
        var tool = Substitute.For<IChatTool>();
        tool.Name.Returns(name);
        tool.Description.Returns($"eval fake {name}");
        tool.IsAvailableAsync(Arg.Any<ChatToolContext>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return tool;
    }
}
