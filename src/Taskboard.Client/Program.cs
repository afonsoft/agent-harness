using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Blazor;
using Taskboard.Blazor.Services;
using Taskboard.GitHub;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlazorBootstrap();
builder.Services.AddAuthorizationCore();

// SPEC-20260915-blazor-wasm-migration: the shared HttpClient flows the session
// cookie (same-origin) and routes 401 responses to /login.
builder.Services.AddTransient<AuthRedirectHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthRedirectHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
});

builder.Services.AddScoped<TaskboardClient>();
builder.Services.AddScoped<TaskboardAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<TaskboardAuthStateProvider>());

builder.Services.AddScoped<IGitHubService, HttpGitHubService>();
builder.Services.AddScoped<IAgentOrchestrationService, HttpAgentOrchestrationService>();
builder.Services.AddScoped<IAgentModelConfigService, HttpAgentModelConfigService>();
builder.Services.AddScoped<IAgentPromptTemplateService, HttpAgentPromptTemplateService>();

await builder.Build().RunAsync();
