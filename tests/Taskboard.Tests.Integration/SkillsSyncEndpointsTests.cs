using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Server;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

public class SkillsSyncEndpointsTests : IClassFixture<SkillsSyncEndpointsTests.SyncFactory>
{
    private const string AdminPassword = "itest-admin-pass";

    private readonly SyncFactory _factory;
    private readonly HttpClient _client;

    public SkillsSyncEndpointsTests(SyncFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public sealed class SyncFactory : WebApplicationFactory<Program>
    {
        public StubSkillsSyncService Stub { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Taskboard:DataDir", Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}"));
            builder.UseSetting("Admin:Password", AdminPassword);
            builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
            builder.ConfigureServices(services =>
                services.AddSingleton<ISkillsSyncService>(Stub));
        }

        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
            var login = await client.PostAsync(
                "/api/login",
                new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("Username", "admin"),
                    new KeyValuePair<string, string>("Password", AdminPassword),
                ]));
            if (login.StatusCode != HttpStatusCode.Redirect)
            {
                throw new InvalidOperationException(
                    $"Test login failed with {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
            }

            return client;
        }
    }

    public sealed class StubSkillsSyncService : ISkillsSyncService
    {
        public int RequestCount;

        public SkillsSyncStatus GetStatus() => new(
            SkillsSyncState.Idle, null, null, "afonsoft/skills", null, []);

        public void RequestSync(IReadOnlyCollection<AgentType>? agents = null) =>
            Interlocked.Increment(ref RequestCount);

        public Task<SkillsSyncStatus> SyncAsync(
            IReadOnlyCollection<AgentType>? agents = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus());
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_GetSyncStatus_Entao_Retorna401()
    {
        // Covers RF-009: sync status requires authentication
        var response = await _client.GetAsync("/api/skills/sync/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostSync_Entao_Retorna401()
    {
        // Covers RF-009: manual sync requires authentication
        var response = await _client.PostAsync("/api/skills/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetSyncStatus_Entao_RetornaSnapshot()
    {
        // Covers RF-008: status endpoint returns the current snapshot
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/skills/sync/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<JsonObject>();
        status.ShouldNotBeNull();
        status!["state"].ShouldNotBeNull();
        status["agents"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostSync_Entao_Retorna202EDisparaSync()
    {
        // Covers RF-008: manual sync is accepted and enqueues a run
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/skills/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Stub.RequestCount.ShouldBe(1);
    }
}
