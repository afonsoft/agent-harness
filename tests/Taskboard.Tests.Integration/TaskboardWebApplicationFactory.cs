using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Taskboard.Server;

namespace Taskboard.Tests.Integration;

/// <summary>
/// Factory for integration tests. Disables the startup skills sync so tests
/// never perform a real git clone or write to the host's home directory.
/// </summary>
public class TaskboardWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
    }
}
