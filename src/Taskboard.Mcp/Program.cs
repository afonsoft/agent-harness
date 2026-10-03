using Taskboard.Domain.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Taskboard.Mcp.Services;

namespace Taskboard.Mcp;

static class Program
{
    // Fixed loopback default (S1075) — overridable via HARNESS_URL / Taskboard:BaseUrl.
    internal const string DefaultHarnessBaseUrl = "http://127.0.0.1:47823";

    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var baseUrl = HarnessEnv.Get("HARNESS_URL")
            ?? builder.Configuration["Taskboard:BaseUrl"]
            ?? DefaultHarnessBaseUrl;

        builder.Services.AddSingleton<ITaskboardApiClient>(new McpTaskboardApiClient(baseUrl));

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
