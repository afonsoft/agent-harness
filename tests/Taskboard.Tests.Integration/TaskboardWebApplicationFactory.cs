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

    /// <summary>
    /// Deterministic workspace root under the per-factory temp dir — tests
    /// create fake repo dirs here to exercise workspace resolution
    /// (SPEC-20260921-ai-code-thread-config RF-003).
    /// </summary>
    public string WorkspaceRoot { get; protected set; } = string.Empty;

    /// <summary>
    /// Enables the Web CLI Agent feature flag for subclass factories (queue/
    /// retry endpoints return 404 otherwise). Off by default — matches prod.
    /// </summary>
    public bool WebCliAgentEnabled { get; protected set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Fresh data dir per factory: admin.json persists the password hash, so a
        // stale file would make Admin:Password a no-op.
        // The developer shell may carry the deployed server's env file
        // (~/.agent-harness/env): HARNESS_ADMIN_* + legacy TASKBOARD_*, Taskboard__* and GITHUB_TOKEN
        // take precedence over UseSetting and would break test hermeticity.
        foreach (var name in new[]
        {
            "HARNESS_ADMIN_USERNAME", "HARNESS_ADMIN_PASSWORD",
            "TASKBOARD_ADMIN_USERNAME", "TASKBOARD_ADMIN_PASSWORD",
            "HARNESS_DATA_DIR", "TASKBOARD_DATA_DIR", "Harness__DataDir", "Taskboard__DataDir",
            "Taskboard__ApiKey", "Harness__ApiKey", "HARNESS_API_KEY", "TASKBOARD_API_KEY",
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
        if (WorkspaceRoot.Length == 0)
        {
            // Subclasses (e.g. SpecsFactory) may pre-seed a workspace with
            // fixtures before the host builds.
            WorkspaceRoot = Path.Combine(dataDir, "repos");
        }

        builder.UseSetting("Taskboard:WorkspaceRoot", WorkspaceRoot);
        // Empty home → cli-metrics locator resolves nothing (Missing), so the
        // startup sync never reads real agent databases on the test host.
        // The dir must exist — TerminalSessionManager uses it as the PTY cwd.
        var homeDir = Path.Combine(dataDir, "home");
        Directory.CreateDirectory(homeDir);
        builder.UseSetting("Taskboard:HomeDir", homeDir);
        // Assistant runs keep the MockLLMProvider echo path in tests —
        // the real CLI backend (SPEC-20260921-ai-chat-cli-backend) is covered
        // by unit tests; integration tests must never spawn agent processes.
        builder.UseSetting("Taskboard:AiChat:MockProvider", "true");
        builder.UseSetting("Taskboard:WebCliAgent:Enabled", WebCliAgentEnabled ? "true" : "false");
        builder.ConfigureServices(services =>
        {
            // GET /api/agents must not depend on which CLIs happen to be on the
            // host's PATH — report every known agent as Available.
            services.RemoveAll<IAgentDiscoveryService>();
            services.AddSingleton<IAgentDiscoveryService>(new FakeAgentDiscoveryService());
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

        public Task<Taskboard.GitHub.IssueDto?> GetIssueAsync(
            string repositoryFullName, int issueNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(issueNumber == Issue.Number ? Issue : null);

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

        public Task<IReadOnlyList<Taskboard.GitHub.MilestoneDto>> GetMilestonesAsync(
            string repositoryFullName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.MilestoneDto>>(
            [
                new Taskboard.GitHub.MilestoneDto(
                    1, "v1.0", DateTimeOffset.UtcNow.AddDays(14), "open")
            ]);

        public Task<IReadOnlyList<Taskboard.GitHub.IssueLabelEventDto>> GetIssueTimelineEventsAsync(
            string repositoryFullName, int issueNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.IssueLabelEventDto>>(
            [
                new Taskboard.GitHub.IssueLabelEventDto(
                    DateTimeOffset.UtcNow.AddDays(-10), "todo", Added: true),
                new Taskboard.GitHub.IssueLabelEventDto(
                    DateTimeOffset.UtcNow.AddDays(-5), "in-progress", Added: true)
            ]);

        private readonly List<Taskboard.GitHub.IssueCommentDto> _comments = [];

        public Task<IReadOnlyList<Taskboard.GitHub.IssueCommentDto>> GetIssueCommentsAsync(
            string repositoryFullName, int issueNumber, int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.IssueCommentDto>>(_comments);

        public Task<Taskboard.GitHub.IssueCommentDto> AddIssueCommentAsync(
            string repositoryFullName, int issueNumber, string body,
            CancellationToken cancellationToken = default)
        {
            var comment = new Taskboard.GitHub.IssueCommentDto(
                _comments.Count + 1, "itest-bot", body, DateTimeOffset.UtcNow, null,
                $"https://github.com/{repositoryFullName}/issues/{issueNumber}#comment");
            _comments.Add(comment);
            return Task.FromResult(comment);
        }

        private static readonly Taskboard.GitHub.WorkflowRunDto WorkflowRun = new(
            Id: 9001,
            Name: "CI",
            DisplayTitle: "feat: sample run",
            RunNumber: 321,
            Event: "push",
            Status: "completed",
            Conclusion: "success",
            HeadBranch: "main",
            HeadSha: "0123456789abcdef",
            ActorLogin: "itest-bot",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            RunStartedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            HtmlUrl: "https://github.com/x/y/actions/runs/9001");

        public Task<IReadOnlyList<Taskboard.GitHub.WorkflowDto>> GetWorkflowsAsync(
            string repositoryFullName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.WorkflowDto>>(
            [
                new Taskboard.GitHub.WorkflowDto(
                    11, "CI", ".github/workflows/dotnet.yml", "active",
                    "https://github.com/x/y/actions/workflows/dotnet.yml", WorkflowRun)
            ]);

        public Task<IReadOnlyList<Taskboard.GitHub.WorkflowRunDto>> GetWorkflowRunsAsync(
            string repositoryFullName, long workflowId, int take = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.GitHub.WorkflowRunDto>>([WorkflowRun]);

        public Task<string> CreatePullRequestAsync(
            string repositoryFullName, string title, string head, string baseBranch,
            string? body, CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://github.com/{repositoryFullName}/pull/7");
    }

    /// <summary>Reports every known agent as Available — PATH-independent discovery.</summary>
    private sealed class FakeAgentDiscoveryService : IAgentDiscoveryService
    {
        private static string BinaryFor(AgentType type)
            => AgentCliMap.CliKindFor(type) is { } kind
                ? AgentCliMap.All.First(kv => kv.Key == kind).Value.Binary
                : type.ToString().ToLowerInvariant();

        public Task<IReadOnlyList<AgentInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentInfo>>(Enum.GetValues<AgentType>()
                .Select(type => new AgentInfo(
                    BinaryFor(type),
                    $"/usr/bin/{BinaryFor(type)}",
                    type,
                    AgentStatus.Available,
                    Version: "itest",
                    Description: null,
                    SupportsInteractiveSession: type is AgentType.OpenCode or AgentType.Claude
                        or AgentType.Codex or AgentType.Devin))
                .ToList());

        public string? ResolveExecutablePath(AgentType agentType)
            => $"/usr/bin/{BinaryFor(agentType)}";
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
    internal sealed class FakeCodeServerManager : Taskboard.Application.Contracts.Vscode.ICodeServerManager
    {
        /// <summary>Status retornado pelos stubs — testes ajustam por cenário.</summary>
        public static Taskboard.Application.Contracts.Vscode.VscodeStatus Status { get; set; } = new(
            Installed: false, BinaryPath: null, Version: null, Running: false,
            Port: 8377, HomeDirectory: "/tmp/itest-home", WorkspaceRoot: "/tmp/itest-home/repos");

        public Task<Taskboard.Application.Contracts.Vscode.VscodeStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public Task<Taskboard.Application.Contracts.Vscode.VscodeStatus> EnsureStartedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public Task<Taskboard.Application.Contracts.Vscode.VscodeStatus> RestartAsync(
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
