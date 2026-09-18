using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Server;

namespace Taskboard.Tests.Integration;

/// <summary>
/// Factory for integration tests. Disables the startup skills sync so tests
/// never perform a real git clone or write to the host's home directory.
/// Also provides a cookie-authenticated client for the now-protected /api
/// surface (SPEC-20260915-api-authorization-hardening).
///
/// <see cref="IAgentCliStatusService"/> is stubbed to report every CLI as
/// installed + authenticated — eligibility behaviour must not depend on the
/// agent CLIs that happen to exist on the machine running the tests.
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
        // The developer shell may carry the deployed server's env file
        // (~/.taskboard/env): TASKBOARD_ADMIN_*, Taskboard__* and GITHUB_TOKEN
        // take precedence over UseSetting and would break test hermeticity.
        foreach (var name in new[]
        {
            "TASKBOARD_ADMIN_USERNAME", "TASKBOARD_ADMIN_PASSWORD",
            "TASKBOARD_DATA_DIR", "Taskboard__DataDir",
            "Taskboard__ApiKey", "TASKBOARD_API_KEY",
            "GITHUB_TOKEN", "GH_TOKEN"
        })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        var dataDir = Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}");
        builder.UseSetting("Taskboard:DataDir", dataDir);
        builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Taskboard:ApiKey", TestApiKey);
        // Deterministic workspace root — the real resolver would create ~/repos
        // on whatever machine runs the tests.
        builder.UseSetting("Taskboard:WorkspaceRoot", Path.Combine(dataDir, "repos"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAgentCliStatusService>();
            services.AddSingleton<IAgentCliStatusService>(new FakeAgentCliStatusService());
            services.RemoveAll<IAgentCliInstallService>();
            services.AddSingleton<IAgentCliInstallService>(new FakeAgentCliInstallService());
            services.RemoveAll<Taskboard.GitHub.IGitHubService>();
            services.AddSingleton<Taskboard.GitHub.IGitHubService>(new FakeGitHubService());
            // Never probe or spawn a real code-server from tests.
            services.RemoveAll<Taskboard.Application.Contracts.Vscode.IVscodeInstallService>();
            services.AddSingleton<Taskboard.Application.Contracts.Vscode.IVscodeInstallService>(
                new FakeVscodeInstallService());
            services.RemoveAll<Taskboard.Application.Contracts.Vscode.ICodeServerManager>();
            services.AddSingleton<Taskboard.Application.Contracts.Vscode.ICodeServerManager>(
                new FakeCodeServerManager());
        });
    }

    /// <summary>
    /// Deterministic GitHub stub — issue mutations are recorded in memory so
    /// endpoint tests never hit api.github.com (SPEC-20260918-kanban-card-ux).
    /// </summary>
    private sealed class FakeGitHubService : Taskboard.GitHub.IGitHubService
    {
        private static readonly Taskboard.GitHub.IssueDto Issue = new(
            Id: 1,
            Number: 42,
            Title: "itest issue",
            Body: "body",
            State: "open",
            Url: "https://api.github.com/x",
            HtmlUrl: "https://github.com/x",
            Labels: ["todo"],
            Column: Taskboard.GitHub.GitHubBoardColumn.Todo,
            AssigneeLogin: null,
            Priority: "None",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            ClosedAt: null);

        public void SetToken(string token)
        {
        }

        public Task<IReadOnlyList<Taskboard.GitHub.RepositoryDto>> GetRepositoriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.RepositoryDto>>([]);

        public Task<IReadOnlyList<Taskboard.GitHub.IssueDto>> GetIssuesAsync(
            string repositoryFullName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.IssueDto>>([Issue]);

        public Task<Taskboard.GitHub.IssueDto> UpdateIssueColumnAsync(
            string repositoryFullName, int issueNumber,
            Taskboard.GitHub.GitHubBoardColumn? oldColumn,
            Taskboard.GitHub.GitHubBoardColumn newColumn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue);

        public Task<Taskboard.GitHub.IssueDto> CreateIssueAsync(
            string repositoryFullName, string title, string? body,
            Taskboard.GitHub.GitHubBoardColumn initialColumn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue);

        public Task AddLabelsToIssueAsync(
            string repositoryFullName, int issueNumber,
            IReadOnlyCollection<string> labels,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Taskboard.GitHub.IssueDto> UpdateIssueAsync(
            string repositoryFullName, int issueNumber, string? title, string? body,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue);

        public Task<Taskboard.GitHub.IssueDto> SetIssuePriorityAsync(
            string repositoryFullName, int issueNumber, string priority,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue);

        public Task<Taskboard.GitHub.IssueDto> CloseIssueAsync(
            string repositoryFullName, int issueNumber, string resolution,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue);
    }

    /// <summary>Reports all known CLIs as installed + authenticated (deterministic eligibility).</summary>
    private sealed class FakeAgentCliStatusService : IAgentCliStatusService
    {
        public Task<IReadOnlyList<AgentCliStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentCliStatus>>(AgentCliMap.All
                .Select(kv => new AgentCliStatus(
                    kv.Key,
                    kv.Value.DisplayName,
                    kv.Value.Binary,
                    Installed: true,
                    Version: "itest",
                    AgentCliAuthStatus.Authenticated,
                    kv.Value.ConfigDirDisplay,
                    kv.Value.LoginCommand,
                    kv.Value.InstallHint,
                    kv.Value.Install.RequiredTool,
                    PrerequisiteMet: true))
                .ToList());
    }

    /// <summary>
    /// Deterministic install stub — endpoint tests must never run real
    /// npm/curl installs on the host (SPEC-20260918-cli-agents-expansion).
    /// </summary>
    private sealed class FakeAgentCliInstallService : IAgentCliInstallService
    {
        public Task<AgentCliInstallStatus> StartInstallAsync(
            AgentCliKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentCliInstallStatus(
                kind, AgentCliInstallState.Succeeded, DateTimeOffset.UtcNow, 0,
                [new AgentCliInstallLine(DateTimeOffset.UtcNow, "info", "fake install completed")]));

        public AgentCliInstallStatus GetStatus(AgentCliKind kind) =>
            new(kind, AgentCliInstallState.Succeeded, DateTimeOffset.UtcNow, 0, []);
    }

    /// <summary>Deterministic code-server status — never installed/running on test hosts.</summary>
    private sealed class FakeCodeServerManager : Taskboard.Application.Contracts.Vscode.ICodeServerManager
    {
        private static readonly Taskboard.Application.Contracts.Vscode.VscodeStatus Status = new(
            Installed: false, BinaryPath: null, Version: null, Running: false,
            Port: 8377, HomeDirectory: "/tmp/itest-home", WorkspaceRoot: "/tmp/itest-home/repos");

        public Task<Taskboard.Application.Contracts.Vscode.VscodeStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public Task<Taskboard.Application.Contracts.Vscode.VscodeStatus> EnsureStartedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }

    /// <summary>Deterministic code-server install stub — never runs curl on the host.</summary>
    private sealed class FakeVscodeInstallService : Taskboard.Application.Contracts.Vscode.IVscodeInstallService
    {
        public Task<Taskboard.Application.Contracts.Vscode.VscodeInstallStatus> StartInstallAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Taskboard.Application.Contracts.Vscode.VscodeInstallStatus(
                AgentCliInstallState.Succeeded, DateTimeOffset.UtcNow, 0,
                [new AgentCliInstallLine(DateTimeOffset.UtcNow, "info", "fake install completed")]));

        public Taskboard.Application.Contracts.Vscode.VscodeInstallStatus GetStatus() =>
            new(AgentCliInstallState.Succeeded, DateTimeOffset.UtcNow, 0, []);
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
