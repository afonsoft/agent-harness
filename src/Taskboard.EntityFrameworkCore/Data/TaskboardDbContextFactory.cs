using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.EntityFrameworkCore.Data;

public sealed class TaskboardDbContextFactory : IDesignTimeDbContextFactory<TaskboardDbContext>
{
    public TaskboardDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().Build();
        var hostEnvironment = new DesignTimeHostEnvironment(Directory.GetCurrentDirectory());
        var environment = new TaskboardEnvironment(configuration, hostEnvironment);
        var dataDir = environment.GetDataDir();
        Directory.CreateDirectory(dataDir);
        var connectionString = $"Data Source={Path.Combine(dataDir, "harness.sqlite")}";

        var optionsBuilder = new DbContextOptionsBuilder<TaskboardDbContext>();
        optionsBuilder.UseSqlite(connectionString, b => b.MigrationsAssembly(typeof(TaskboardDbContext).Assembly.FullName));

        return new TaskboardDbContext(optionsBuilder.Options);
    }
}
