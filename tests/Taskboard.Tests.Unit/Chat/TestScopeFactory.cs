using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Taskboard.Application.Contracts.Agents;
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
}
