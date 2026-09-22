using Taskboard.Domain.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Taskboard.Mcp.Services;

namespace Taskboard.Mcp;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var baseUrl = HarnessEnv.Get("HARNESS_URL")
            ?? builder.Configuration["Taskboard:BaseUrl"]
            ?? "http://127.0.0.1:47823";

        builder.Services.AddSingleton<ITaskboardApiClient>(new TaskboardApiClient(baseUrl));

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
