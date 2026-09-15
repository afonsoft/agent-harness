using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Taskboard.Blazor.Services;

/// <summary>
/// DelegatingHandler on the shared HttpClient
/// (SPEC-20260915-blazor-wasm-migration): a 401 on a non-auth endpoint
/// invalidates the cached identity and routes to <c>/login</c>. The state
/// provider is resolved lazily — it depends on the HttpClient this handler
/// wraps, so constructor injection would be a cycle.
/// </summary>
public sealed class AuthRedirectHandler(NavigationManager nav, IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path.StartsWith("/api/login", StringComparison.Ordinal)
            || path.StartsWith("/api/auth", StringComparison.Ordinal))
        {
            return response;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            services.GetRequiredService<TaskboardAuthStateProvider>().Invalidate();
            if (!nav.Uri.Contains("/login", StringComparison.Ordinal))
            {
                nav.NavigateTo("/login");
            }
        }

        return response;
    }
}
