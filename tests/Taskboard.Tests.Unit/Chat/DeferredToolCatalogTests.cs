using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D01/D02/D03 — deferred tool catalog:
/// availability probes (TTL-cached, fail-closed), config-driven deferral,
/// meta-tool immunity, append-only promotion and the wire def assembly.
/// </summary>
public sealed class DeferredToolCatalogTests
{
    private sealed class FakeTool(
        string name,
        string description = "desc",
        Func<ChatToolContext, CancellationToken, Task<bool>>? probe = null) : IChatTool
    {
        public int ProbeCalls;
        public string Name => name;
        public string Description => description;
        public string ParametersJson => """{"type":"object","properties":{"x":{"type":"string"}}}""";

        public Task<bool> IsAvailableAsync(ChatToolContext context, CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return probe?.Invoke(context, cancellationToken) ?? Task.FromResult(true);
        }

        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult("{}"));
    }

    private static DeferredToolCatalog NewCatalog(
        Dictionary<string, string?>? config = null, TimeSpan? probeTtl = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(), probeTtl: probeTtl);

    private static ChatToolContext Ctx => TestScopeFactory.Ctx();

    private static Dictionary<string, IChatTool> ToolSet(params IChatTool[] tools) =>
        tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    [Fact]
    public async Task Dado_ToolSetPadrao_Quando_PlanAsync_Entao_DefaultsFicamDeferidos()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("shell_exec"), new FakeTool("browser_use"),
            new FakeTool("generate_image"), new FakeTool("code_interpreter"), new FakeTool("tool_search"));

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.ActiveNames.ShouldBe(["shell_exec", "tool_search"]);
        plan.Deferred.Select(e => e.Name).ShouldBe(["browser_use", "code_interpreter", "generate_image"]);
        plan.Deferred.ShouldAllBe(e => e.Available);
    }

    [Fact]
    public async Task Dado_ProbeFalha_Quando_PlanAsync_Entao_DeferredIndisponivel()
    {
        var catalog = NewCatalog();
        var flaky = new FakeTool("browser_use", probe: (_, _) => Task.FromResult(false));
        var set = ToolSet(new FakeTool("shell_exec"), flaky);

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.ActiveNames.ShouldBe(["shell_exec"]);
        var entry = plan.Deferred.ShouldHaveSingleItem();
        entry.Name.ShouldBe("browser_use");
        entry.Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ProbeLanca_Quando_PlanAsync_Entao_FailClosed()
    {
        var catalog = NewCatalog();
        var broken = new FakeTool("browser_use", probe: (_, _) => throw new InvalidOperationException("boom"));
        var set = ToolSet(broken);

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.Deferred.ShouldHaveSingleItem().Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ProbeLento_Quando_PlanAsync_Entao_TimeoutIndisponivel()
    {
        var catalog = NewCatalog();
        var slow = new FakeTool("browser_use", probe: async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return true;
        });
        var set = ToolSet(slow);

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.Deferred.ShouldHaveSingleItem().Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ProbeCached_Quando_ReplanDentroDoTtl_Entao_NaoReprova()
    {
        var catalog = NewCatalog();
        var tool = new FakeTool("generate_image");
        var set = ToolSet(tool);

        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        tool.ProbeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_TtlExpirado_Quando_Replan_Entao_Reprova()
    {
        var catalog = NewCatalog(probeTtl: TimeSpan.FromMilliseconds(50));
        var tool = new FakeTool("generate_image");
        var set = ToolSet(tool);

        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);
        await Task.Delay(120);
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        tool.ProbeCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_MetaToolNaConfig_Quando_PlanAsync_Entao_NuncaDeferida()
    {
        var catalog = NewCatalog(new() { [DeferredToolCatalog.ConfigKey] = """["tool_search","shell_exec"]""" });
        var set = ToolSet(new FakeTool("tool_search"), new FakeTool("shell_exec"));

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.ActiveNames.ShouldContain("tool_search");
        plan.Deferred.Select(e => e.Name).ShouldBe(["shell_exec"]);
    }

    [Fact]
    public async Task Dado_ConfigVazia_Quando_PlanAsync_Entao_ListaSubstituiDefaults()
    {
        var catalog = NewCatalog(new() { [DeferredToolCatalog.ConfigKey] = "[]" });
        var set = ToolSet(new FakeTool("browser_use"), new FakeTool("shell_exec"));

        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        plan.Deferred.ShouldBeEmpty();
        plan.ActiveNames.ShouldBe(["browser_use", "shell_exec"]);
    }

    [Fact]
    public async Task Dado_ToolDeferidaDisponivel_Quando_Activate_Entao_Promovida()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var result = catalog.Activate("c1", "browser_use");

        result.Ok.ShouldBeTrue();
        result.Activated.ShouldBe(1);
        catalog.PromotedNames("c1").ShouldBe(["browser_use"]);
    }

    [Fact]
    public async Task Dado_ToolIndisponivel_Quando_Activate_Entao_Erro()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use", probe: (_, _) => Task.FromResult(false)));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var result = catalog.Activate("c1", "browser_use");

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("unavailable");
        catalog.PromotedNames("c1").ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ToolAtiva_Quando_Activate_Entao_ErroJaAtiva()
    {
        var catalog = NewCatalog(new() { [DeferredToolCatalog.ConfigKey] = "[]" });
        var set = ToolSet(new FakeTool("shell_exec"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var result = catalog.Activate("c1", "shell_exec");

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("already active");
    }

    [Fact]
    public async Task Dado_ToolDesconhecida_Quando_Activate_Entao_Erro()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("shell_exec"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var result = catalog.Activate("c1", "nope");

        result.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Estrela_Quando_Activate_Entao_PromoveTodasDisponiveis()
    {
        var catalog = NewCatalog();
        var set = ToolSet(
            new FakeTool("browser_use"),
            new FakeTool("generate_image"),
            new FakeTool("code_interpreter"),
            new FakeTool("dead_tool", probe: (_, _) => Task.FromResult(false)));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var result = catalog.Activate("c1", "*");

        result.Ok.ShouldBeTrue();
        result.Activated.ShouldBe(3);
        catalog.PromotedNames("c1").ShouldBe(["browser_use", "code_interpreter", "generate_image"]);
    }

    [Fact]
    public async Task Dado_Query_Quando_Search_Entao_CasaNomeEDescricao()
    {
        var catalog = NewCatalog();
        var set = ToolSet(
            new FakeTool("browser_use", "Drive the headless browser"),
            new FakeTool("generate_image", "Generate an image from a prompt"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        catalog.Search("c1", "browser").Select(e => e.Name).ShouldBe(["browser_use"]);
        catalog.Search("c1", "image").Select(e => e.Name).ShouldBe(["generate_image"]);
        catalog.Search("c1", null).Count.ShouldBe(2);
        catalog.Search("c1", "zzz").ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_PromotedPersiste_Quando_Replan_Entao_AindaPromovido()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);
        catalog.Activate("c1", "browser_use");

        var replanned = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        catalog.PromotedNames("c1").ShouldBe(["browser_use"]);
        replanned.Deferred.Select(e => e.Name).ShouldContain("browser_use");
    }

    [Fact]
    public async Task Dado_EstadosIsolados_Quando_ConversasDiferentes_Entao_NaoVaza()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use"));
        await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);
        catalog.Activate("c1", "browser_use");

        catalog.PromotedNames("c2").ShouldBeEmpty();
        catalog.Deferred("c2").ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_DefsWire_Quando_Promovida_Entao_AppendSemReordenar()
    {
        var catalog = NewCatalog();
        var set = ToolSet(
            new FakeTool("shell_exec"), new FakeTool("read_file"),
            new FakeTool("browser_use"), new FakeTool("generate_image"));
        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);

        var before = DeferredToolWire.BuildDefinitions(set, plan, catalog.PromotedNames("c1"));
        before.Select(d => d.Name).ShouldBe(["read_file", "shell_exec"]);

        catalog.Activate("c1", "browser_use");
        var after = DeferredToolWire.BuildDefinitions(set, plan, catalog.PromotedNames("c1"));

        // Append-only: prefix intacto, schema promovido entra no fim.
        after.Select(d => d.Name).ShouldBe(["read_file", "shell_exec", "browser_use"]);
    }

    [Fact]
    public async Task Dado_PayloadDeSchemas_Quando_Deferidos_Entao_MenorQueBaseline()
    {
        var catalog = NewCatalog();
        var heavy = """{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"string"},"c":{"type":"string"},"d":{"type":"string"},"e":{"type":"string"},"f":{"type":"string"},"g":{"type":"string"},"h":{"type":"string"},"i":{"type":"string"},"j":{"type":"string"}}}""";
        var allTools = Enumerable.Range(0, 20).Select(i => new FakeTool($"tool_{i:d2}")).Cast<IChatTool>().ToList();
        allTools.Add(new HeavyTool("browser_use", heavy));
        allTools.Add(new HeavyTool("generate_image", heavy));
        allTools.Add(new HeavyTool("code_interpreter", heavy));
        var set = ToolSet(allTools.ToArray());

        var baseline = DeferredToolWire.BuildDefinitions(set, plan: null, promoted: null);
        var plan = await catalog.PlanAsync("c1", set, Ctx, CancellationToken.None);
        var planned = DeferredToolWire.BuildDefinitions(set, plan, catalog.PromotedNames("c1"));

        var baselineBytes = JsonSerializer.Serialize(baseline).Length;
        var plannedBytes = JsonSerializer.Serialize(planned).Length;
        plannedBytes.ShouldBeLessThan(baselineBytes);
        planned.Count.ShouldBe(baseline.Count - 3);
    }

    private sealed class HeavyTool(string name, string schema) : IChatTool
    {
        public string Name => name;
        public string Description => name;
        public string ParametersJson => schema;
        public Task<ChatToolResult> ExecuteAsync(
            JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult("{}"));
    }

    [Fact]
    public async Task Dado_ToolSearch_Quando_Executa_Entao_RetornaPares()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use", "Drive the headless browser"));
        await catalog.PlanAsync("conv-x", set, Ctx, CancellationToken.None);
        var tool = new ToolSearchTool(catalog);

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"browser"}""").RootElement, Ctx with { ConversationId = "conv-x" },
            CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Json);
        var matches = doc.RootElement.GetProperty("matches");
        matches.GetArrayLength().ShouldBe(1);
        matches[0].GetProperty("name").GetString().ShouldBe("browser_use");
        matches[0].GetProperty("description").GetString()!.ShouldContain("browser");
    }

    [Fact]
    public async Task Dado_ToolActivate_Quando_Executa_Entao_PromoveProximaChamada()
    {
        var catalog = NewCatalog();
        var set = ToolSet(new FakeTool("browser_use"));
        await catalog.PlanAsync("conv-x", set, Ctx, CancellationToken.None);
        var tool = new ToolActivateTool(catalog);

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"name":"browser_use"}""").RootElement, Ctx with { ConversationId = "conv-x" },
            CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        catalog.PromotedNames("conv-x").ShouldBe(["browser_use"]);
        var plan = await catalog.PlanAsync("conv-x", set, Ctx, CancellationToken.None);
        DeferredToolWire.BuildDefinitions(set, plan, catalog.PromotedNames("conv-x"))
            .Select(d => d.Name).ShouldBe(["browser_use"]);
    }

    [Fact]
    public async Task Dado_ToolActivateSemNome_Quando_Executa_Entao_Recusa()
    {
        var tool = new ToolActivateTool(NewCatalog());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement, Ctx, CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }
}
