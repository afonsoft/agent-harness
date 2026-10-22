using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// Scope factory backed by a real ServiceProvider so the delegation tools can
/// resolve the scoped <see cref="IAgentCliDefinitionRepository"/> — substitutes
/// default to an empty def list (SPEC-20261004 RF-007).
/// </summary>
internal static class TestScopeFactory
{
    public static IServiceScopeFactory Empty() =>
        WithDefs(Substitute.For<IAgentCliDefinitionRepository>());

    public static IServiceScopeFactory WithDefs(IAgentCliDefinitionRepository defs) =>
        new ServiceCollection()
            .AddSingleton(defs)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>SPEC-20261020-v2: scope factory resolving a delegation-service substitute.</summary>
    public static IServiceScopeFactory WithDelegation(IDelegationService service) =>
        new ServiceCollection()
            .AddSingleton(service)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>SPEC-20261020-v2 P3: delegation + launch resolver for launch-pref tests.</summary>
    public static IServiceScopeFactory WithDelegationAndLaunch(
        IDelegationService service, IAgentLaunchResolver resolver) =>
        new ServiceCollection()
            .AddSingleton(service)
            .AddSingleton(resolver)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>SPEC-20261021-worktree-setup-scripts P3: worktree services
    /// for worktree_status / worktree_setup tool tests.</summary>
    public static IServiceScopeFactory WithWorktree(
        Taskboard.Application.Contracts.Harness.IWorktreeSessionRepository sessions,
        Taskboard.Application.Contracts.Harness.IWorktreeCardService cards,
        Taskboard.Application.Contracts.Harness.IWorktreeStatusProbe probe,
        Taskboard.Application.Contracts.Harness.IWorktreeYamlReader? yamlReader = null,
        Taskboard.Application.Contracts.Harness.IWorktreeSetupRunner? setupRunner = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(cards)
            .AddSingleton(probe);
        if (yamlReader is not null)
        {
            services.AddSingleton(yamlReader);
        }

        if (setupRunner is not null)
        {
            services.AddSingleton(setupRunner);
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>Shared chat-tool context for tool unit tests.</summary>
    public static ChatToolContext Ctx(string? defaultAgentCli = "codex") =>
        new(
            WorkspacePath: Path.GetTempPath(),
            ProviderId: Guid.NewGuid(),
            ProviderBaseUrl: "http://provider.test",
            ProviderApiKey: "sk",
            ImageModel: "",
            SearchBackend: "none",
            SearchUrl: "",
            SearchApiKey: "",
            ConversationId: "conv-1",
            Model: "m1",
            DelegationDepth: 0,
            Activity: null,
            ToolSet: null,
            DefaultAgentCli: defaultAgentCli,
            DefaultAgentModel: null);
}
