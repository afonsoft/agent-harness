using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Server.Services;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>Constrói um JobRegistry real sobre um store em memória.</summary>
public static class JobRegistryTestHost
{
    public static JobRegistry Create(InMemoryJobScheduleStore? store = null, params JobDefinition[] definitions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJobScheduleStore>(store ?? new InMemoryJobScheduleStore());
        var provider = services.BuildServiceProvider();
        return new JobRegistry(
            definitions,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobRegistry>.Instance);
    }
}
