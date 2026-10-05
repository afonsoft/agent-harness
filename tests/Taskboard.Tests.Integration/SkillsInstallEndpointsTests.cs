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

        public string? LastRepository { get; private set; }
        public (string Repository, string? Skill)? LastSkillInstall { get; private set; }
        public SkillsInstallStep? NextSkillStep { get; set; }

        public void RequestInstall(string? repository = null) => InstallRequests++;

        public Task<SkillsInstallStatus> InstallAsync(
            string? repository = null, CancellationToken cancellationToken = default)
        {
            LastRepository = repository;
            return Task.FromResult(GetStatus());
        }

        public Task<SkillsInstallStatus> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus());

        public Task<SkillsInstallStep> InstallSkillAsync(
            string repository, string? skill, CancellationToken cancellationToken = default)
        {
            LastSkillInstall = (repository, skill);
            return Task.FromResult(NextSkillStep ?? new SkillsInstallStep(
                "npx-add-skill", SkillsInstallStepState.Succeeded, 0, 10, "ok"));
        }
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
                // SPEC-20261010-mcp-skills-hub: deterministic skills search —
                // never spawn a real `npx skills find` in tests.
                services.AddSingleton(new Taskboard.Integrations.Skills.SkillsSearchService(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Taskboard.Integrations.Skills.SkillsSearchService>.Instance,
                    runner: new FakeSearchRunner(),
                    executableLocator: _ => "/usr/bin/npx"));
            });
        }

        /// <summary>Canned <c>npx skills find</c> output.</summary>
        public sealed class FakeSearchRunner : Taskboard.Integrations.Skills.ISkillsInstallRunner
        {
            public IReadOnlyList<string>? LastArguments { get; private set; }

            public Task<Taskboard.Integrations.Skills.CommandResult> RunAsync(
                string executable, string workingDirectory,
                IReadOnlyList<string> arguments, CancellationToken cancellationToken)
            {
                LastArguments = arguments;
                return Task.FromResult(new Taskboard.Integrations.Skills.CommandResult(
                    0, "afonsoft/skills@code-review — Review a PR\nowner/pack - collection\n", ""));
            }
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

    // ---- SPEC-20261010-mcp-skills-hub RF-004/RF-005 ----

    [Fact]
    public async Task Dado_RepoValido_Quando_InstallRepo_Entao_202ComOverride()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/skills/install-repo",
            new { repository = "owner/pack" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Installer.LastRepository.ShouldBe("owner/pack");
    }

    [Fact]
    public async Task Dado_RepoInvalido_Quando_InstallRepo_Entao_400()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/skills/install-repo",
            new { repository = "not a repo" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_QueryValida_Quando_Search_Entao_200ComResultados()
    {
        var client = await CreateAuthenticatedClientAsync();

        var results = await client.GetFromJsonAsync<JsonArray>("/api/skills/search?q=review");

        results.ShouldNotBeNull();
        results.Count.ShouldBe(2);
        results[0]!["repository"]!.GetValue<string>().ShouldBe("afonsoft/skills");
    }

    [Fact]
    public async Task Dado_SemQuery_Quando_Search_Entao_400()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/skills/search?q=%20");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_InstallOneValido_Quando_Post_Entao_202()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/skills/install-one",
            new { repository = "afonsoft/skills", skill = "qa-analyst" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Installer.LastSkillInstall.ShouldBe(("afonsoft/skills", "qa-analyst"));
    }

    [Fact]
    public async Task Dado_InstallOneSkillInvalida_Quando_Post_Entao_400()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/skills/install-one",
            new { repository = "afonsoft/skills", skill = "Bad Skill!" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_StepFalhou_Quando_InstallOne_Entao_502()
    {
        var client = await CreateAuthenticatedClientAsync();
        _factory.Installer.NextSkillStep = new SkillsInstallStep(
            "npx-add-skill", SkillsInstallStepState.Failed, 1, 10, "boom");

        var response = await client.PostAsJsonAsync("/api/skills/install-one",
            new { repository = "afonsoft/skills" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        _factory.Installer.NextSkillStep = null;
    }
}
