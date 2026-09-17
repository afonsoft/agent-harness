using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Skills;
using Taskboard.Server;
using Xunit;

namespace Taskboard.Tests.Integration;

public class SkillsInstallEndpointsTests : IClassFixture<SkillsInstallEndpointsTests.InstallFactory>
{
    private const string AdminPassword = "itest-admin-pass";

    private readonly InstallFactory _factory;
    private readonly HttpClient _client;

    public SkillsInstallEndpointsTests(InstallFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public sealed class FakeInstaller : ISkillsInstallerService
    {
        public int InstallRequests { get; private set; }

        public SkillsInstallStatus GetStatus() => new(
            SkillsSyncState.Idle,
            Installed: false,
            SkillCount: 0,
            LastRunUtc: null,
            LastDurationMs: null,
            Repository: "afonsoft/skills",
            Prerequisites: new Dictionary<string, bool> { ["npx"] = true, ["git"] = true, ["bash"] = true },
            Steps: [],
            Locations: [],
            Error: null);

        public void RequestInstall() => InstallRequests++;

        public Task<SkillsInstallStatus> InstallAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus());

        public Task<SkillsInstallStatus> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus());
    }

    public sealed class InstallFactory : WebApplicationFactory<Program>
    {
        private HttpClient? _authedClient;

        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}");
        public string HomeDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-home-{Guid.NewGuid()}");
        public FakeInstaller Installer { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Taskboard:DataDir", DataDir);
            builder.UseSetting("Taskboard:HomeDir", HomeDir);
            builder.UseSetting("Admin:Password", AdminPassword);
            builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISkillsInstallerService>(Installer);
            });
        }

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
    public async Task Dado_SemAutenticacao_Quando_GetInstallStatus_Entao_401()
    {
        var response = await _client.GetAsync("/api/skills/install/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostInstall_Entao_401()
    {
        var response = await _client.PostAsync("/api/skills/install", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostVerify_Entao_401()
    {
        var response = await _client.PostAsync("/api/skills/install/verify", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetInstallStatus_Entao_200ComPayload()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/skills/install/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["state"].ShouldNotBeNull();
        body["installed"]!.GetValue<bool>().ShouldBeFalse();
        body["repository"]!.GetValue<string>().ShouldBe("afonsoft/skills");
        body["prerequisites"]!["npx"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostInstall_Entao_202()
    {
        var client = await CreateAuthenticatedClientAsync();

        var first = await client.PostAsync("/api/skills/install", content: null);
        var second = await client.PostAsync("/api/skills/install", content: null);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = JsonNode.Parse(await first.Content.ReadAsStringAsync())!.AsObject();
        body["state"].ShouldNotBeNull();
        _factory.Installer.InstallRequests.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostVerify_Entao_200ComStatus()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/skills/install/verify", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["installed"].ShouldNotBeNull();
        body["skillCount"].ShouldNotBeNull();
    }
}
