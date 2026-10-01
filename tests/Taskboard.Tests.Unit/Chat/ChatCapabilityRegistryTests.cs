using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Skills;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-chat-capability-registry FR-002/003: masters por kind +
/// lista Disabled governam o tool set efetivo e o catálogo.
/// </summary>
public class ChatCapabilityRegistryTests
{
    private static ChatCapabilityRegistry Build(
        IReadOnlyDictionary<string, IChatTool> tools,
        IReadOnlyList<SkillDto>? skills = null,
        Dictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();
        return new ChatCapabilityRegistry(tools, new StubSkills(skills ?? []), configuration);
    }

    private static IChatTool Tool(string name, string? capabilityId = null, ChatCapabilityKind kind = ChatCapabilityKind.BuiltinTool) =>
        new StubTool(name, capabilityId ?? $"tool:{name}", kind);

    [Fact]
    public async Task Dado_ToolDesabilitada_Quando_Resolve_Entao_AusenteDoToolSet()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>
            {
                ["shell_exec"] = Tool("shell_exec"),
                ["read_file"] = Tool("read_file"),
            },
            config: new() { ["Taskboard:Chat:Capabilities:Disabled"] = """["tool:shell_exec"]""" });

        var set = await registry.ResolveToolSetAsync();

        set.Keys.ShouldBe(["read_file"]);
    }

    [Fact]
    public async Task Dado_MasterToolsOff_Quando_Resolve_Entao_SemTools()
    {
        var registry = Build(
            new Dictionary<string, IChatTool> { ["echo"] = Tool("echo") },
            config: new() { ["Taskboard:Chat:Tools:Enabled"] = "false" });

        (await registry.ResolveToolSetAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_McpTool_Quando_MasterToolsOff_Entao_Removida()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>
            {
                ["mcp_github_get_file"] = Tool("mcp_github_get_file", "mcp:github/get_file", ChatCapabilityKind.McpTool),
            },
            config: new() { ["Taskboard:Chat:Tools:Enabled"] = "false" });

        (await registry.ResolveToolSetAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SkillsHabilitadas_Quando_Lista_Entao_SkillsNoCatalogo()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>(),
            skills:
            [
                new SkillDto("composio-cli", "operate composio", "agents", "/x/composio-cli"),
                new SkillDto("graphify", "knowledge graph", "agents", "/x/graphify"),
            ]);

        var list = await registry.ListAsync();

        list.Where(c => c.Kind == ChatCapabilityKind.Skill).Select(c => c.Id)
            .ShouldBe(["skill:composio-cli", "skill:graphify"]);
        list.ShouldAllBe(c => c.Enabled);
    }

    [Fact]
    public async Task Dado_MasterSkillsOff_Quando_Lista_Entao_SemSkills()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>(),
            skills: [new SkillDto("x", "d", "agents", "/x")],
            config: new() { ["Taskboard:Chat:Skills:Enabled"] = "false" });

        var list = await registry.ListAsync();

        list.ShouldNotContain(c => c.Kind == ChatCapabilityKind.Skill);
    }

    [Fact]
    public async Task Dado_MesmaSkillEmVariasOrigens_Quando_Lista_Entao_SomenteAgents()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>(),
            skills:
            [
                new SkillDto("abp-angular", "d", "agents", "/a/abp-angular"),
                new SkillDto("abp-angular", "d", "claude", "/c/abp-angular"),
                new SkillDto("abp-angular", "d", "devin", "/d/abp-angular"),
                new SkillDto("graphify", "knowledge", "agents", "/a/graphify"),
                new SkillDto("claude-only", "d", "claude", "/c/claude-only"),
            ]);

        var list = await registry.ListAsync();

        list.GroupBy(c => c.Id).ShouldAllBe(g => g.Count() == 1);
        list.Count(c => c.Kind == ChatCapabilityKind.Skill).ShouldBe(2);
        list.ShouldAllBe(c => c.Origin == "agents");
        list.ShouldNotContain(c => c.Name == "claude-only");
    }

    [Fact]
    public async Task Dado_SkillDesabilitada_Quando_Lista_Entao_EnabledFalse()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>(),
            skills: [new SkillDto("x", "d", "agents", "/x")],
            config: new() { ["Taskboard:Chat:Capabilities:Disabled"] = """["skill:x"]""" });

        var capability = (await registry.ListAsync()).Single(c => c.Id == "skill:x");

        capability.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Catalogo_Quando_Lista_Entao_AgrupaPorKindComOrigem()
    {
        var registry = Build(
            new Dictionary<string, IChatTool>
            {
                ["shell_exec"] = Tool("shell_exec"),
                ["run_agent"] = Tool("run_agent", "agent:run", ChatCapabilityKind.AgentDelegation),
            },
            skills: [new SkillDto("s1", "d", "agents", "/s1")]);

        var list = await registry.ListAsync();

        list.Select(c => c.Kind).Distinct().ShouldBe(
            [ChatCapabilityKind.BuiltinTool, ChatCapabilityKind.Skill, ChatCapabilityKind.AgentDelegation]);
        list.Single(c => c.Id == "skill:s1").Origin.ShouldBe("agents");
        list.Single(c => c.Id == "tool:shell_exec").RequiresConfirmation.ShouldBeTrue();
    }

    [Fact]
    public void Dado_IdDesconhecidoEmDisabled_Quando_Checa_Entao_IgnoradoSemErro()
    {
        var registry = Build(
            new Dictionary<string, IChatTool> { ["x"] = Tool("x") },
            config: new() { ["Taskboard:Chat:Capabilities:Disabled"] = """["tool:gone","skill:stale"]""" });

        registry.IsCapabilityEnabled("tool:x").ShouldBeTrue();
    }

    private sealed class StubTool(string name, string capabilityId, ChatCapabilityKind kind) : IChatTool
    {
        public string Name => name;
        public string Description => $"stub {name}";
        public string ParametersJson => """{"type":"object"}""";
        public string CapabilityId => capabilityId;
        public ChatCapabilityKind Kind => kind;

        public Task<ChatToolResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatToolResult("{}"));
    }

    private sealed class StubSkills(IReadOnlyList<SkillDto> skills) : ISkillDiscoveryService
    {
        public Task<IReadOnlyList<SkillDto>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(skills);

        public Task<SkillDetailDto?> GetDetailAsync(
            string source, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<SkillDetailDto?>(null);

        public Task<SkillFileResult> GetFileAsync(
            string source, string name, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkillFileResult(SkillFileError.NotFound, null, null));
    }
}
