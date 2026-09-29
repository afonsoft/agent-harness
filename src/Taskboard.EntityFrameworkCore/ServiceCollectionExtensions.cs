using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Repositories;

namespace Taskboard.EntityFrameworkCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskboardEntityFrameworkCore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TaskboardDbContext>(options =>
        {
            options.UseSqlite(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(TaskboardDbContext).Assembly.FullName);
            });
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfCoreRepository<>));
        services.AddScoped<IJobScheduleStore, EfCoreJobScheduleStore>();

        return services;
    }
}
