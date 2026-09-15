using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Taskboard.Blazor.Services;

/// <summary>
/// Authentication state backed by <c>/api/auth/me</c>
/// (SPEC-20260915-blazor-wasm-migration). The result is cached; call
/// <see cref="Invalidate"/> after logout or a 401 to re-notify consumers.
/// </summary>
public sealed class TaskboardAuthStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private AuthenticationState? _cached;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        ClaimsPrincipal principal;
        try
        {
            var me = await http.GetFromJsonAsync<MeResponse>("/api/auth/me");
            principal = me?.Authenticated == true
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, me.Username ?? string.Empty)],
                    authenticationType: "cookie"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (HttpRequestException)
        {
            principal = new ClaimsPrincipal(new ClaimsIdentity());
        }

        _cached = new AuthenticationState(principal);
        return _cached;
    }

    /// <summary>Drop the cache and re-notify consumers (after logout or a 401).</summary>
    public void Invalidate()
    {
        _cached = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private sealed record MeResponse(bool Authenticated, string? Username);
}
