using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Taskboard.Application.Contracts.Agents;

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
}
