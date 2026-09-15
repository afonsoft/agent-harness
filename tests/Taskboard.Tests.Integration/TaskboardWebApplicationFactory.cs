using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Taskboard.Server;

namespace Taskboard.Tests.Integration;

/// <summary>
/// Factory for integration tests. Disables the startup skills sync so tests
/// never perform a real git clone or write to the host's home directory.
/// Also provides a cookie-authenticated client for the now-protected /api
/// surface (SPEC-20260915-api-authorization-hardening).
/// </summary>
public class TaskboardWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "itest-admin-pass";
    public const string TestApiKey = "itest-api-key-0123456789";

    private HttpClient? _authedClient;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Fresh data dir per factory: admin.json persists the password hash, so a
        // stale file would make Admin:Password a no-op.
        builder.UseSetting("Taskboard:DataDir", Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}"));
        builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Taskboard:ApiKey", TestApiKey);
    }

    // A single login is shared across tests — /api/login is rate limited to 5/minute.
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        if (_authedClient is not null)
        {
            return _authedClient;
        }

        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var login = await client.PostAsync(
            "/api/login",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("Username", "admin"),
                new KeyValuePair<string, string>("Password", AdminPassword),
            ]));
        if (login.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException(
                $"Test login failed with {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        }

        _authedClient = client;
        return client;
    }

    /// <summary>Client that authenticates via the X-Api-Key header (machine-client path).</summary>
    public HttpClient CreateApiKeyClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }
}
