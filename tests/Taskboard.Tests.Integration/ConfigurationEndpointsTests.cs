using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Taskboard.Server;
using Xunit;

namespace Taskboard.Tests.Integration;

public class ConfigurationEndpointsTests : IClassFixture<ConfigurationEndpointsTests.AuthenticatedFactory>
{
    private const string AdminPassword = "itest-admin-pass";

    private readonly AuthenticatedFactory _factory;
    private readonly HttpClient _client;

    public ConfigurationEndpointsTests(AuthenticatedFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public sealed class AuthenticatedFactory : WebApplicationFactory<Program>
    {
        private HttpClient? _authedClient;

        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Taskboard:DataDir", DataDir);
            builder.UseSetting("Admin:Password", AdminPassword);
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
    }

    private Task<HttpClient> CreateAuthenticatedClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_GetConfiguration_Entao_Retorna401()
    {
        // Covers RF-006: effective values require authentication
        var response = await _client.GetAsync("/api/configuration");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PutConfiguration_Entao_Retorna401()
    {
        // Covers RF-006: mutations require authentication
        var response = await _client.PutAsJsonAsync(
            "/api/configuration/Logging:LogLevel:Default", new { value = "Warning" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetConfiguration_Entao_RetornaCatalogo()
    {
        // Covers RF-003 / RF-006: authenticated GET returns the typed catalog
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/configuration");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        var entries = result?["entries"] as JsonArray;
        entries.ShouldNotBeNull();
        var keys = entries!.Select(e => e!["key"]!.GetValue<string>()).ToList();
        keys.ShouldContain("Taskboard:Port");
        keys.ShouldContain("Logging:LogLevel:Default");
        keys.ShouldContain("ConnectionStrings:Taskboard");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PutOverrideValido_Entao_204ESourceDb()
    {
        // Covers RF-004 / AC: PUT persists and next GET reports source=db
        var client = await CreateAuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(
            "/api/configuration/Logging:LogLevel:Default", new { value = "Warning" });

        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await client.GetAsync("/api/configuration");
        var result = await get.Content.ReadFromJsonAsync<JsonObject>();
        var entry = (result?["entries"] as JsonArray)!
            .Select(e => e!)
            .Single(e => e["key"]!.GetValue<string>() == "Logging:LogLevel:Default");
        entry["source"]!.GetValue<string>().ShouldBe("db");
        entry["effectiveValue"]!.GetValue<string>().ShouldBe("Warning");

        // Covers RF-005: reset removes the override
        var delete = await client.DeleteAsync("/api/configuration/Logging:LogLevel:Default");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/api/configuration/Taskboard:Port", "abc")]
    [InlineData("/api/configuration/Taskboard:Port", "99999")]
    [InlineData("/api/configuration/Logging:LogLevel:Default", "Verbose")]
    [InlineData("/api/configuration/Foo:Bar", "x")]
    public async Task Dado_ValorInvalidoOuChaveDesconhecida_Quando_Put_Entao_400Validation(string url, string value)
    {
        // Covers RF-004: validation failures and unknown keys return 400 VALIDATION
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(url, new { value });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result!["error"]!["code"]!.GetValue<string>().ShouldBe("VALIDATION");
    }

    [Theory]
    [InlineData("/api/configuration/Taskboard:DataDir")]
    [InlineData("/api/configuration/ConnectionStrings:Taskboard")]
    [InlineData("/api/configuration/Admin:Username")]
    public async Task Dado_ChaveReadOnly_Quando_Put_Entao_400KeyReadOnly(string url)
    {
        // Covers RF-004: read-only keys return 400 KEY_READ_ONLY
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(url, new { value = "/x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result!["error"]!["code"]!.GetValue<string>().ShouldBe("KEY_READ_ONLY");
    }

    [Fact]
    public async Task Dado_SemOverride_Quando_Delete_Entao_404()
    {
        // Covers RF-005: deleting a non-existent override returns 404
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync("/api/configuration/AllowedHosts");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result!["error"]!["code"]!.GetValue<string>().ShouldBe("OVERRIDE_NOT_FOUND");
    }

    [Fact]
    public async Task Dado_ConnectionString_Quando_GetConfiguration_Entao_ValorMascarado()
    {
        // Covers RF-003 / AC: secrets are never returned in clear text
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/configuration");
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        var entry = (result?["entries"] as JsonArray)!
            .Select(e => e!)
            .Single(e => e["key"]!.GetValue<string>() == "ConnectionStrings:Taskboard");

        entry["masked"]!.GetValue<bool>().ShouldBeTrue();
        var value = entry["effectiveValue"]!.GetValue<string>();
        value.ShouldStartWith("••••");
        value.ShouldNotContain("Data Source");
    }
}
