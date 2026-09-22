using Taskboard.Domain.Shared.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.EntityFrameworkCore;
using Taskboard;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.CliMetrics;
using Taskboard.Application.GitHub;
using Taskboard.Application.Harness;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Application.Specs;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Domain.Issues;
using Taskboard.Issues;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore;
using Taskboard.EntityFrameworkCore.Agents;
using Taskboard.EntityFrameworkCore.CliMetrics;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Harness;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
using Taskboard.Application.Configuration;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.CliDb;
using Taskboard.Integrations.CliDb.Extractors;
using Taskboard.Integrations.Configuration;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.GitHub;
using Taskboard.Integrations.Harness;
using Taskboard.Integrations.Harness.Context;
using Taskboard.Integrations.Harness.Security;
using Taskboard.Integrations.Harness.Verification;
using Taskboard.Integrations.Jira;
using Taskboard.Integrations.Mcp;
using Taskboard.Integrations.Terminal;
using Taskboard.Integrations.Skills;
using Taskboard.Integrations.Specs;
using Taskboard.Integrations.Vscode;
using Taskboard.Integrations.Workspace;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Application.Contracts.Vscode;
using Yarp.ReverseProxy.Configuration;
using Taskboard.Application.Settings;
using Taskboard.GitHub;
using Taskboard.Json;
using Taskboard.Server.HealthChecks;
using Taskboard.Server.Hubs;
using Taskboard.Repositories;
using Taskboard.Requests;
using Taskboard.Server.Api;
using Taskboard.Server.Mapping;
using Taskboard.Server.Middleware;
using Taskboard.Server.Serialization;
using Taskboard.Server.Services;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using Taskboard.Mcp.Services;
using Taskboard.Mcp.Tools;
using Taskboard.ValueObjects;

var builder = WebApplication.CreateBuilder(args);

// SPEC-20260922-harness-home-rename: HARNESS__* env vars bind to the internal
// Taskboard:* config section (HARNESS__ACP__SESSIONRUNS → Taskboard:Acp:SessionRuns).
// Added after the default env provider so HARNESS__* beats Taskboard__*; the
// SQLite overrides provider (added below) still wins over both.
var harnessEnvOverrides = Environment.GetEnvironmentVariables()
    .Cast<System.Collections.DictionaryEntry>()
    .Where(e => e.Key is string k && k.StartsWith("HARNESS__", StringComparison.Ordinal))
    .ToDictionary(
        e => "Taskboard:" + ((string)e.Key)["HARNESS__".Length..].Replace("__", ":", StringComparison.Ordinal),
        e => (string?)e.Value?.ToString());
if (harnessEnvOverrides.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(harnessEnvOverrides);
}

var environment = new TaskboardEnvironment(builder.Configuration, builder.Environment);
var dataDir = environment.GetDataDir();
Directory.CreateDirectory(dataDir);

// SPEC-20260922-harness-home-rename RF-004: migrate the legacy database file
// in place — idempotent, only when the new name does not exist yet.
var legacyDbPath = Path.Combine(dataDir, "taskboard.sqlite");
var harnessDbPath = Path.Combine(dataDir, "harness.sqlite");
if (!File.Exists(harnessDbPath) && File.Exists(legacyDbPath))
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        if (File.Exists(legacyDbPath + suffix))
        {
            File.Move(legacyDbPath + suffix, harnessDbPath + suffix);
        }
    }
}

// Database-stored overrides are registered last so they win over env vars and
// appsettings. Loaded now so a Taskboard:Port override applies to this boot.
var sqliteConfigProvider = new SqliteConfigurationProvider(harnessDbPath);
builder.Configuration.Sources.Add(new SqliteConfigurationSource(sqliteConfigProvider));
builder.Services.AddSingleton(sqliteConfigProvider);

var serverUrls = environment.GetServerUrls();
builder.WebHost.UseUrls(serverUrls);

builder.Services.AddOptions<TaskboardOptions>()
    .BindConfiguration("Taskboard")
    .ValidateOnStart();

builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration("Admin");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSingleton<IEventStreamService, InMemoryEventStreamService>();
builder.Services.AddSingleton<IThreadEventStreamService, InMemoryThreadEventStreamService>();
builder.Services.AddSingleton<AiCatalogService>();
builder.Services.AddScoped<AiChatCatalogService>();
builder.Services.AddSingleton<ICliChatRunner, CliChatRunner>();
builder.Services.AddSingleton<CloudSessionService>();
builder.Services.AddSingleton<IJiraService, JiraService>();
builder.Services.AddSingleton<IExecutableResolver, CodexExecutableResolver>();
builder.Services.AddSingleton<IProcessTreeSignaler, ProcessTreeSignaler>();

// AI Chat services
builder.Services.AddSingleton<ILLMProvider, MockLLMProvider>();
builder.Services.AddScoped<AiChatService>();

builder.Services.AddHttpClient<JiraService>();

builder.Services.AddSignalR(options =>
{
    // Backgrounded browser tabs throttle the JS timers driving SignalR's
    // application-level keep-alive to ~1/minute. With the default 30s client
    // timeout the server kills those connections and every PTY bound to them.
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
});
builder.Services.AddAntiforgery();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Harness API",
        Version = "v1",
        Description = "API for agent-harness projects, tasks, agents and skills."
    });
});
builder.Services.AddSingleton<IGitHubService, GitHubService>();
builder.Services.AddSingleton<IAgentDiscoveryService, AgentDiscoveryService>();
builder.Services.AddSingleton<ISkillDiscoveryService>(sp => new SkillDiscoveryService(new[]
{
    new SkillDiscoverySource("claude", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills")),
    new SkillDiscoverySource("devin", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devin", "skills")),
    new SkillDiscoverySource("cursor", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "skills")),
    new SkillDiscoverySource("opencode", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "skills")),
    new SkillDiscoverySource("taskboard", Path.Join(AppContext.BaseDirectory, "skills"))
}));
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<RuntimeConfigurationService>();
builder.Services.AddSingleton<IAgentAdapter>(sp =>
    new KnownCliAgentAdapter(sp.GetRequiredService<WorkspaceService>()));
// SPEC-20260921-acp-v1-conformance: one-shot runs go through a real ACP
// session when Taskboard:Acp:SessionRuns=true; otherwise keep args-mode.
builder.Services.AddSingleton<IAgentAcpClient>(sp =>
{
    var fallback = new JsonRpcAcpClient(sp.GetServices<IAgentAdapter>());
    return sp.GetRequiredService<IConfiguration>().GetValue("Taskboard:Acp:SessionRuns", false)
        ? new AcpSessionRunClient(sp.GetRequiredService<AcpSessionClient>(), fallback)
        : (IAgentAcpClient)fallback;
});
// ACP session options: timeouts + client fs/terminal surface + RAG MCP
// passthrough into session/new.mcpServers (RF-011/012).
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var options = new AcpSessionOptions
    {
        ClientFs = cfg.GetValue("Taskboard:Acp:ClientFs", true),
        ClientTerminal = cfg.GetValue("Taskboard:Acp:ClientTerminal", false),
        TerminalAuth = cfg.GetValue("Taskboard:Acp:TerminalAuth", true),
        BooleanConfigOptions = cfg.GetValue("Taskboard:Acp:BooleanConfigOptions", true),
        RequestTimeout = TimeSpan.FromSeconds(cfg.GetValue("Taskboard:Acp:RequestTimeoutSeconds", 60)),
        TurnTimeout = TimeSpan.FromMinutes(cfg.GetValue("Taskboard:Acp:TurnTimeoutMinutes", 30)),
        HandshakeTimeout = TimeSpan.FromSeconds(cfg.GetValue("Taskboard:Acp:HandshakeTimeoutSeconds", 15)),
        PermissionTimeout = TimeSpan.FromMinutes(cfg.GetValue("Taskboard:Acp:PermissionTimeoutMinutes", 10)),
        AgentTcpPort = cfg.GetValue<int?>("Taskboard:Acp:TcpPort"),
        // SPEC-20260921-acp-v2-readiness: v2 is strictly opt-in while draft.
        MaxProtocolVersion = cfg.GetValue("Taskboard:Acp:MaxProtocolVersion", 1),
    };
    var ragUrl = cfg["Taskboard:Rag:Url"];
    if (!string.IsNullOrWhiteSpace(ragUrl))
    {
        var ragApiKey = cfg["Taskboard:Rag:ApiKey"];
        options.McpServers =
        [
            new AcpMcpServerSpec(
                cfg["Taskboard:Rag:ServerName"] ?? "knowledge",
                ragUrl,
                Command: null,
                Args: [],
                Headers: string.IsNullOrWhiteSpace(ragApiKey)
                    ? null
                    : new Dictionary<string, string> { ["Authorization"] = $"Bearer {ragApiKey}" }),
        ];
    }
    return options;
});
builder.Services.AddSingleton<IAcpClientToolHandler, AcpClientToolHandler>();
builder.Services.AddSingleton<AcpSessionClient>(sp =>
    new AcpSessionClient(
        sp.GetServices<IAgentAdapter>(),
        sp.GetRequiredService<AcpSessionOptions>(),
        sp.GetService<IAcpClientToolHandler>()));
builder.Services.AddSingleton<IAgentSessionClient>(sp => sp.GetRequiredService<AcpSessionClient>());
// SPEC-20260921-ai-code-thread-config RF-005: modelos reportados pela sessão ACP.
builder.Services.AddSingleton<IAgentSessionModelCatalog, AcpSessionModelCatalog>();
builder.Services.AddSingleton<PermissionGate>();
builder.Services.AddSingleton<AgentSessionManager>();
builder.Services.AddSingleton<IAgentLogBroadcaster, SignalRAgentLogBroadcaster>();
builder.Services.AddScoped<IAgentLogRepository, EfCoreAgentLogRepository>();
builder.Services.AddScoped<IAgentRunRepository, EfCoreAgentRunRepository>();
// SPEC-20260921-agent-execution-event-pipeline: normalized sink + durable
// replay. Taskboard:AgentEvents:Enabled=false is the documented fast-rollback
// path — events are dropped at the sink and replay returns empty.
builder.Services.AddScoped<IAgentRunEventRepository, EfCoreAgentRunEventRepository>();
builder.Services.AddScoped<AgentControlService>();
builder.Services.AddSingleton<ISecretRedactor>(sp => sp.GetRequiredService<SecretScrubber>());
if (builder.Configuration.GetValue("Taskboard:AgentEvents:Enabled", true))
{
    builder.Services.AddSingleton<IAgentExecutionEventSink, AgentExecutionEventSink>();
    builder.Services.AddHostedService<AgentRunEventRetentionService>();
}
else
{
    builder.Services.AddSingleton<IAgentExecutionEventSink, NullAgentExecutionEventSink>();
}
builder.Services.AddScoped<IWorktreeSessionRepository, EfCoreWorktreeSessionRepository>();
builder.Services.AddSingleton<IGitCommandRunner, GitCommandRunner>();
builder.Services.AddScoped<IAgentEligibilityService, AgentEligibilityService>();
builder.Services.AddScoped<IAgentModelConfigService, AgentModelConfigService>();
builder.Services.AddScoped<ITimelineMetricsService, TimelineMetricsService>();
builder.Services.AddSingleton<IAgentOrchestrationService, AgentOrchestrationService>();
builder.Services.AddHostedService(sp => (AgentOrchestrationService)sp.GetRequiredService<IAgentOrchestrationService>());

// Test/override seam: agent config + skills writes target this directory.
var homeDir = builder.Configuration["Taskboard:HomeDir"]
    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

builder.Services.AddScoped<IWorkspaceIsolationService>(sp => new GitWorktreeManager(
    sp.GetRequiredService<IGitCommandRunner>(),
    sp.GetRequiredService<IWorktreeSessionRepository>(),
    // SPEC-20260919-harness-workspace-isolation: default ~/repos (Taskboard:WorktreeRoot).
    WorktreePaths.ResolveRoot(builder.Configuration["Taskboard:WorktreeRoot"], homeDir),
    sp.GetRequiredService<ILogger<GitWorktreeManager>>()));
builder.Services.AddScoped<IMemoryService, EfCoreMemoryService>();
builder.Services.AddScoped<IContextCompiler>(sp => new ProjectContextCompiler(
    sp.GetRequiredService<IGitCommandRunner>(),
    sp.GetService<IMemoryService>(),
    sp.GetRequiredService<ILogger<ProjectContextCompiler>>()));
builder.Services.AddSingleton<IContextCompactor, ContextCompactor>();
// SPEC-20260919-harness-security-permission-gateway: singletons puros (sem estado).
builder.Services.AddSingleton<ICommandRiskClassifier, DynamicCommandClassifier>();
builder.Services.AddSingleton<PathJailValidator>();
builder.Services.AddSingleton<SecretScrubber>();
builder.Services.AddSingleton<IPermissionGateway, PermissionGateway>();
// SPEC-20260919-harness-verification-loop: motor + evidência + loop fechado.
builder.Services.AddSingleton<IProcessRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IVerificationEngine, DotNetVerificationEngine>();
builder.Services.AddScoped<IVerificationReportRepository, EfCoreVerificationReportRepository>();
builder.Services.AddScoped<IVerificationLoop, VerificationLoop>();
// SPEC-20260919-cli-db-reader: acesso read-only aos SQLite dos CLIs gerenciados.
builder.Services.AddSingleton<ICliDatabaseLocator>(sp => new CliDatabaseLocator(
    homeDir,
    sp.GetRequiredService<ILogger<CliDatabaseLocator>>()));
builder.Services.AddSingleton<ICliDatabaseReader>(sp => new SqliteCliDatabaseReader(
    homeDir,
    sp.GetRequiredService<ILogger<SqliteCliDatabaseReader>>()));

// SPEC-20260919-cli-metrics: ingestão incremental das fontes do cli-db-reader.
// Options resolvem antes dos extratores — SPEC-20260922 RF-002 injeta o
// CliTokenEstimator nos que estimam tokens.
var cliMetricsOptions = builder.Configuration
    .GetSection("Taskboard:CliMetrics")
    .Get<CliMetricsOptions>() ?? new CliMetricsOptions();
builder.Services.AddSingleton(cliMetricsOptions);
builder.Services.AddSingleton(sp => new CliTokenEstimator(
    sp.GetRequiredService<CliMetricsOptions>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new CodexCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<CodexCliDbExtractor>>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new OpenCodeCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<OpenCodeCliDbExtractor>>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new DevinCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<DevinCliDbExtractor>>(),
    sp.GetRequiredService<CliTokenEstimator>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new AntigravityCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<AntigravityCliDbExtractor>>(),
    sp.GetRequiredService<CliTokenEstimator>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new ClineCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<ClineCliDbExtractor>>(),
    sp.GetRequiredService<CliTokenEstimator>()));
builder.Services.AddSingleton<ICliDbExtractor>(sp => new ClaudeContextModeCliDbExtractor(
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<ICliDatabaseReader>(),
    sp.GetRequiredService<ILogger<ClaudeContextModeCliDbExtractor>>(),
    sp.GetRequiredService<CliTokenEstimator>()));
builder.Services.AddScoped<ICliMetricsRepository, EfCoreCliMetricsRepository>();
builder.Services.AddScoped<ICliUsageMetricsProvider, EfCoreCliUsageMetricsProvider>();
builder.Services.AddScoped<ICliMetricsService>(sp => new CliMetricsService(
    sp.GetRequiredService<ICliMetricsRepository>(),
    sp.GetServices<ICliDbExtractor>(),
    sp.GetRequiredService<ICliDatabaseLocator>(),
    sp.GetRequiredService<CliMetricsOptions>(),
    sp.GetRequiredService<ILogger<CliMetricsService>>()));
builder.Services.AddSingleton<CliMetricsSyncCoordinator>();
builder.Services.AddHostedService<CliMetricsSyncService>();

// SPEC-20260919-ade-multi-agent-orchestration: DAG de agentes especializados
// sobre worktree compartilhado do run.
builder.Services.AddSingleton<PipelineEngine>();
builder.Services.AddScoped<IPipelineOrchestrator, PipelineExecutionAppService>();
builder.Services.AddHostedService<PipelineEngineService>();
// SPEC-20260919-ade-cockpit-hitl: cockpit event stream (buffered replay +
// SignalR broadcast) e fila in-memory de steer do RF-003.
builder.Services.AddSingleton<ISteerQueue, SteerQueue>();
builder.Services.AddSingleton<ICockpitEventStream, CockpitEventStream>();
// SPEC-20260919-ade-observability-finops: métricas de custo + budget caps.
// SPEC-20260922-finops-dashboard-detail: Taskboard:FinOps (active window).
builder.Services.AddSingleton(builder.Configuration
    .GetSection("Taskboard:FinOps").Get<FinOpsOptions>() ?? new FinOpsOptions());
builder.Services.AddScoped<IFinOpsService>(sp => new FinOpsService(
    sp.GetRequiredService<IRepository<RunCostMetric>>(),
    sp.GetRequiredService<IRepository<ModelPriceRate>>(),
    sp.GetRequiredService<IRepository<CliDailyUsageAggregate>>(),
    sp.GetRequiredService<IRepository<CliSessionMetric>>(),
    sp.GetRequiredService<IRepository<CliMetricSource>>(),
    sp.GetRequiredService<IRepository<AgentRun>>(),
    sp.GetRequiredService<FinOpsOptions>(),
    TimeProvider.System));
// SPEC-20260920-harness-recurring-jobs: projeção de custo sobre uso CLI a cada 30s.
builder.Services.AddScoped<FinOpsAggregator>();
builder.Services.AddHostedService<FinOpsAggregationService>();
// SPEC-20260920-harness-maintenance-jobs: reaper de runs stale + drift scan horário.
builder.Services.AddScoped<StaleRunReaper>();
builder.Services.AddHostedService<StaleAgentRunReaperService>();
builder.Services.AddSingleton<SpecDriftReportCache>();
builder.Services.AddHostedService<SpecDriftScanService>();

// SPEC-20260919-ade-living-specs: catálogo vivo das specs .specs/SPEC-*.md.
builder.Services.AddSingleton<ISpecDocumentParser, MarkdigSpecParser>();
builder.Services.AddSingleton<ISpecAppService, SpecAppService>();
builder.Services.AddSingleton<ISpecDriftDetector, SpecDriftDetector>();

// SPEC-20260919-ade-observability-finops RF-004: a ActivitySource
// "Taskboard.Harness" emite spans localmente sem custo; exportação OTLP só é
// ativada quando explicitamente configurada (guardrail §8 — nenhum dado sai
// do processo sem consentimento em appsettings.json).
var otlpEndpoint = builder.Configuration["Taskboard:Telemetry:OtlpEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource(HarnessTelemetrySource.Name)
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

builder.Services.AddSingleton<SkillsOperationLog>();
builder.Services.AddSingleton<McpOperationLog>();

builder.Services.AddSingleton<ISkillsSyncService>(sp => new SkillsSyncService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<SkillsSyncService>>(),
    Path.Join(dataDir, "skills-cache"),
    homeDir,
    async ct => await ResolveEnabledAgentsAsync(sp, ct),
    async ct => await ResolveGitHubTokenAsync(sp, ct),
    sp.GetRequiredService<SkillsOperationLog>()));

builder.Services.AddSingleton<ISkillsInstallerService>(sp => new SkillsInstallerService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<SkillsInstallerService>>(),
    dataDir,
    homeDir,
    async ct => await ResolveGitHubTokenAsync(sp, ct),
    log: sp.GetRequiredService<SkillsOperationLog>()));

builder.Services.AddSingleton<IMcpProvisioningService>(sp => new McpProvisioningService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<McpProvisioningService>>(),
    homeDir,
    async ct => await ResolveEnabledAgentsAsync(sp, ct),
    sp.GetRequiredService<McpOperationLog>()));

builder.Services.AddSingleton<IAgentCliStatusService>(sp => new AgentCliStatusService(
    homeDir,
    sp.GetRequiredService<ILogger<AgentCliStatusService>>()));

builder.Services.AddSingleton<IAgentModelCatalogService>(sp => new AgentModelCatalogService(
    homeDir,
    sp.GetRequiredService<ILogger<AgentModelCatalogService>>()));

builder.Services.AddSingleton<IAgentCliInstallService>(sp => new AgentCliInstallService(
    homeDir,
    sp.GetRequiredService<ILogger<AgentCliInstallService>>()));

builder.Services.AddSingleton(sp => new PtySessionFactory(
    homeDir,
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<TerminalSessionManager>();

// SPEC-20260917-vscode-web-workspace: ~/repos workspace + code-server managed process.
var vscodePort = builder.Configuration.GetValue("Taskboard:Vscode:Port", CodeServerProcessManager.DefaultPort);

builder.Services.AddSingleton(sp =>
{
    var workspace = new WorkspaceService(
        builder.Configuration["Taskboard:WorkspaceRoot"],
        homeDir,
        sp.GetRequiredService<ILogger<WorkspaceService>>());
    workspace.EnsureRoot();
    return workspace;
});
builder.Services.AddSingleton<Taskboard.Application.Contracts.Workspace.IWorkspacePathResolver>(
    sp => sp.GetRequiredService<WorkspaceService>());

builder.Services.AddSingleton<IVscodeInstallService>(sp => new VscodeInstallService(
    homeDir,
    sp.GetRequiredService<ILogger<VscodeInstallService>>()));

builder.Services.AddSingleton(sp => new CodeServerProcessManager(
    homeDir,
    vscodePort,
    sp.GetRequiredService<WorkspaceService>(),
    sp.GetRequiredService<ILogger<CodeServerProcessManager>>()));
builder.Services.AddSingleton<ICodeServerManager>(sp => sp.GetRequiredService<CodeServerProcessManager>());

// YARP: /vscode/** → loopback code-server (auth'd by the app; code-server itself
// runs --auth none and is never reachable off-box).
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [
            new RouteConfig
            {
                RouteId = "vscode",
                ClusterId = "vscode",
                AuthorizationPolicy = "Default",
                Match = new RouteMatch { Path = "/vscode/{**catch-all}" },
                Transforms =
                [
                    new Dictionary<string, string> { ["PathRemovePrefix"] = "/vscode" },
                    new Dictionary<string, string> { ["RequestHeader"] = "X-Forwarded-Prefix", ["Set"] = "/vscode" }
                ]
            }
        ],
        [
            new ClusterConfig
            {
                ClusterId = "vscode",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new() { Address = $"http://127.0.0.1:{vscodePort}/" }
                }
            }
        ]);

// Opt-out switch for environments where a background git clone must not run
// (tests, air-gapped hosts). Default: enabled.
if (builder.Configuration.GetValue("Taskboard:Skills:SyncOnStartup", true))
{
    builder.Services.AddHostedService<SkillsSyncHostedService>();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/agent-log-hub"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return System.Threading.Tasks.Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/agent-log-hub"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return System.Threading.Tasks.Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return System.Threading.Tasks.Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, Taskboard.Server.Auth.ApiKeyAuthenticationHandler>(
        Taskboard.Server.Auth.ApiKeyAuthenticationHandler.SchemeName, null);

// SPEC-20260915-api-authorization-hardening: the default policy accepts either
// the cookie session or the X-Api-Key scheme, so every RequireAuthorization()
// covers browsers and machine clients (taskctl, MCP) alike.
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(
        CookieAuthenticationDefaults.AuthenticationScheme,
        Taskboard.Server.Auth.ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes =
    [
        "text/plain",
        "text/css",
        "text/html",
        "application/javascript",
        "application/json",
        "image/svg+xml"
    ];
});
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("ReadOnlyApi", p => p.Expire(TimeSpan.FromSeconds(60)));
    options.AddPolicy("StaticAssets", p => p.Expire(TimeSpan.FromDays(1)));
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("api", policy =>
    {
        policy.PermitLimit = 100;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 0;
    });
});
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy("Alive."), tags: ["live"])
    .AddCheck("data-directory", () =>
    {
        var dataDir = environment.GetDataDir();
        return Directory.Exists(dataDir)
            ? HealthCheckResult.Healthy("Data directory exists.")
            : HealthCheckResult.Unhealthy($"Data directory does not exist: {dataDir}");
    }, tags: ["data"])
    .AddCheck<TaskboardDbContextHealthCheck>("database", tags: ["db"]);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRequestLocalization(options =>
{
    options.SupportedCultures = [new System.Globalization.CultureInfo("en-US"), new System.Globalization.CultureInfo("pt-BR")];
    options.SupportedUICultures = options.SupportedCultures;
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US");
});

builder.Services.AddSingleton(AdminUser.CreateFromConfiguration(builder.Configuration, dataDir));

var connectionString = $"Data Source={harnessDbPath}";

builder.Services.AddTaskboardEntityFrameworkCore(connectionString);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new StringIdJsonConverterFactory());
    options.SerializerOptions.Converters.Add(new StringValueObjectJsonConverterFactory());
});

// SPEC-20260919-mcp-v2-http-transport RF-002/RF-004/RF-005: the same
// TaskboardTools served by the stdio exe are also exposed over Streamable
// HTTP (stateless by default in the v2 SDK) under the authenticated /api
// group. The loopback ITaskboardApiClient lets the tools reuse the local
// API unchanged (it authenticates itself via TASKBOARD_API_KEY).
builder.Services.AddSingleton<ITaskboardApiClient>(sp =>
    new TaskboardApiClient(
        HarnessEnv.Get("HARNESS_URL")
        ?? builder.Configuration["Taskboard:BaseUrl"]
        ?? "http://127.0.0.1:47823",
        sp.GetRequiredService<IConfiguration>()["Taskboard:ApiKey"]));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(TaskboardTools).Assembly);

var app = builder.Build();

// Behind nginx/Cloudflare: honor X-Forwarded-Proto/For (loopback proxy is
// trusted by default) so redirects and cookie policies see the real scheme.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseExceptionHandler();
app.UseCors("Dev");
app.UseResponseCompression();
app.UseRequestLocalization();
app.UseRouting();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TaskboardDbContext>();
    await dbContext.Database.MigrateAsync();

    // The overrides table may have just been created by this migration.
    sqliteConfigProvider.Reload();
}

// SPEC-20260915-api-authorization-hardening RF-001/RF-002: the whole API
// requires an authenticated principal (cookie or X-Api-Key); only the
// endpoints below carry AllowAnonymous.
var api = app.MapGroup("/api").RequireAuthorization();

// SPEC-20260919-harness-workspace-isolation §5: worktree lifecycle endpoints.
var harness = api.MapGroup("harness");

harness.MapPost("worktrees", async (
        CreateWorktreeRequestDto request,
        IWorkspaceIsolationService isolation,
        CancellationToken ct) =>
{
    var session = await isolation.CreateWorktreeAsync(
        request.RunId,
        request.RepositoryPath,
        request.BaseBranch ?? "main",
        request.TaskSlug,
        request.RetainOnFailure,
        ct);
    return Results.Created($"/api/harness/worktrees/{session.RunId}", session);
});

harness.MapGet("worktrees/{runId}", async (
    string runId,
    IWorkspaceIsolationService isolation,
    CancellationToken ct) =>
    await isolation.GetAsync(runId, ct) is { } session
        ? Results.Ok(session)
        : Results.NotFound());

harness.MapGet("worktrees/{runId}/diff", async (
    string runId,
    IWorkspaceIsolationService isolation,
    CancellationToken ct) =>
    Results.Ok(await isolation.GetDiffAsync(runId, ct)));

// SPEC-20260921-cockpit-live-logs-explorer-diff RF-003: read-only explorer —
// lazy directory listing + capped file content, paths confined to the worktree.
harness.MapGet("worktrees/{runId}/files", async (
    string runId,
    string? path,
    IWorkspaceIsolationService isolation,
    CancellationToken ct) =>
{
    if (await isolation.GetAsync(runId, ct) is null)
    {
        return Results.NotFound();
    }

    return await isolation.ListFilesAsync(runId, path, ct) is { } list
        ? Results.Ok(list)
        : Results.NotFound();
});

harness.MapGet("worktrees/{runId}/files/content", async (
    string runId,
    string path,
    IWorkspaceIsolationService isolation,
    CancellationToken ct) =>
{
    if (await isolation.GetAsync(runId, ct) is null)
    {
        return Results.NotFound();
    }

    return await isolation.ReadFileAsync(runId, path, ct) is { } file
        ? Results.Ok(file)
        : Results.NotFound();
});

harness.MapDelete("worktrees/{runId}", async (
    string runId,
    bool force,
    IWorkspaceIsolationService isolation,
    CancellationToken ct) =>
{
    await isolation.RemoveWorktreeAsync(runId, force, ct);
    return Results.NoContent();
});

// SPEC-20260919-harness-context-memory §5: context compilation + memory API.
harness.MapPost("context/compile", async (
        CompileContextRequestDto request,
        IContextCompiler compiler,
        CancellationToken ct) =>
    Results.Ok(await compiler.CompileAsync(
        request.WorktreePath,
        request.AgentType,
        request.MaxTokenBudget,
        ct)));

harness.MapPost("memory", async (
        AddMemoryRequestDto request,
        IMemoryService memory,
        CancellationToken ct) =>
{
    var type = Enum.TryParse<MemoryType>(request.Type, ignoreCase: true, out var parsed)
        ? parsed
        : MemoryType.Fact;
    var item = await memory.AddMemoryAsync(
        request.RepositoryFullName,
        request.Topic,
        request.Content,
        request.Tags,
        type,
        ct);
    return Results.Created($"/api/harness/memory/{item.Id}", item);
});

harness.MapGet("memory", async (
    string repositoryFullName,
    string? query,
    int? take,
    IMemoryService memory,
    CancellationToken ct) =>
    Results.Ok(string.IsNullOrWhiteSpace(query)
        ? await memory.ListAsync(repositoryFullName, take ?? 100, ct)
        : await memory.SearchAsync(repositoryFullName, query, take ?? 10, ct)));

harness.MapDelete("memory/{id}", async (
    string id,
    IMemoryService memory,
    CancellationToken ct) =>
{
    await memory.DeleteAsync(id, ct);
    return Results.NoContent();
});

// SPEC-20260919-harness-security-permission-gateway §5: pre-dispatch evaluation.
harness.MapPost("security/evaluate", async (
        SecurityEvaluateRequestDto request,
        IPermissionGateway gateway,
        CancellationToken ct) =>
{
    var policy = Enum.TryParse<SecurityPolicyMode>(request.Policy, ignoreCase: true, out var parsed)
        ? parsed
        : SecurityPolicyMode.Standard;
    return Results.Ok(await gateway.EvaluateAsync(
        request.ToolName,
        request.Command,
        request.WorktreePath,
        policy,
        ct));
});

// SPEC-20260919-harness-verification-loop §5: single verification pass +
// persisted evidence. The correction loop lives in IVerificationLoop /
// AgentOrchestrationService (VerifySolutionFile opt-in).
harness.MapPost("verification/run", async (
        VerificationRunRequestDto request,
        IVerificationEngine engine,
        IVerificationReportRepository reports,
        CancellationToken ct) =>
{
    var report = await engine.RunAsync(request, ct);
    await reports.SaveAsync(request.WorktreePath, report, request.Attempt, ct);
    return Results.Ok(report);
});

// SPEC-20260919-ade-multi-agent-orchestration §5: pipeline DAG endpoints.
var pipelines = api.MapGroup("harness/pipelines");
pipelines.MapGet("templates", async (IPipelineOrchestrator orchestrator, CancellationToken ct) =>
    Results.Ok(await orchestrator.ListTemplatesAsync(ct)));
pipelines.MapPost("start", async (
        PipelineStartRequest request,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
{
    var dto = await orchestrator.StartAsync(request, ct);
    return Results.Created($"/api/harness/pipelines/{dto.PipelineExecutionId}", dto);
});
pipelines.MapGet("{id}", async (
        string id,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    await orchestrator.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
pipelines.MapPost("{id}/stages/{stageKey}/approve", async (
        string id,
        string stageKey,
        PipelineApproveRequest request,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    Results.Ok(await orchestrator.ApproveStageAsync(id, stageKey, request.Comment, ct)));
pipelines.MapPost("{id}/stages/{stageKey}/retry", async (
        string id,
        string stageKey,
        PipelineRetryRequest request,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    Results.Accepted(value: await orchestrator.RetryStageAsync(id, stageKey, request.AdjustedPrompt, ct)));
pipelines.MapPost("{id}/cancel", async (
        string id,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    Results.Ok(await orchestrator.CancelAsync(id, ct)));

// SPEC-20260919-ade-cockpit-hitl §5: runs (pipeline executions) para o cockpit.
var runs = api.MapGroup("harness/runs");
runs.MapGet("", async (
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    Results.Ok(await orchestrator.ListAsync(cancellationToken: ct)));
runs.MapPost("", async (
        RunStartRequest request,
        IPipelineOrchestrator orchestrator,
        WorkspaceService workspace,
        IRepository<IssueHistoryEvent> history,
        CancellationToken ct) =>
{
    // The client sends only owner/repo — the repo path resolves server-side
    // and stays confined to the workspace root.
    var repositoryPath = workspace.ResolveCardWorkdir(request.RepositoryFullName, out _);
    var prompt = string.IsNullOrWhiteSpace(request.SpecPath)
        ? request.Prompt
        : $"{request.Prompt}\n\nSpec: `{request.SpecPath}`";
    var dto = await orchestrator.StartAsync(
        new PipelineStartRequest(
            request.TemplateId,
            request.RepositoryFullName,
            repositoryPath,
            request.BaseBranch,
            request.IssueId,
            prompt,
            request.MaxBudgetUsd,
            request.AgentOverride,
            request.TierOverride,
            request.SkipVerification),
        ct);
    if (!string.IsNullOrWhiteSpace(request.IssueId))
    {
        await RecordIssueHistoryByIdAsync(
            history, request.IssueId, request.RepositoryFullName,
            IssueHistoryEventKind.RunStarted, ct, detail: dto.PipelineExecutionId);
    }

    return Results.Created($"/api/harness/runs/{dto.PipelineExecutionId}", dto);
});

// SPEC-20260920-cockpit-pause-resume: pause/resume entre estágios — 404 run
// inexistente, 409 transição inválida (InvalidPipelineState).
runs.MapPost("{id}/pause", async (
        string id,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    await orchestrator.PauseAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
runs.MapPost("{id}/resume", async (
        string id,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
    await orchestrator.ResumeAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
runs.MapGet("{id}", async (
        string id,
        IPipelineOrchestrator orchestrator,
        IFinOpsService finOps,
        IWorkspaceIsolationService isolation,
        CancellationToken ct) =>
    await orchestrator.GetAsync(id, ct) is { } execution
        ? Results.Ok(new RunDetailsDto(
            execution,
            await finOps.GetRunTelemetryAsync(id, ct),
            await isolation.GetAsync(id, ct)))
        : Results.NotFound());
runs.MapGet("{id}/events", (
        string id,
        ICockpitEventStream stream) =>
    Results.Ok(stream.GetBuffered(id)));
runs.MapPost("{id}/steer", async (
        string id,
        SteerRequest request,
        ISteerQueue steer,
        ICockpitEventStream stream,
        CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Instruction))
    {
        return Results.BadRequest(new { error = "Instruction cannot be empty." });
    }

    steer.Enqueue(id, request.Instruction.Trim());
    await stream.PublishAsync(
        new CockpitEventDto(id, DateTimeOffset.UtcNow, "steer", "Steer queued", request.Instruction.Trim()),
        ct);
    return Results.Accepted();
});
runs.MapPost("{id}/approvals/{requestId}", async (
        string id,
        string requestId,
        ApprovalReplyRequest request,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
{
    // `stage:<key>` requestIds resolve back to the stage gate (RF-004);
    // anything else is an unknown request.
    if (!requestId.StartsWith("stage:", StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    var stageKey = requestId["stage:".Length..];
    var execution = string.Equals(request.Action, "Allow", StringComparison.OrdinalIgnoreCase)
        ? await orchestrator.ApproveStageAsync(id, stageKey, request.Comment, ct)
        : await orchestrator.RejectStageAsync(id, stageKey, request.Comment, ct);
    return Results.Ok(execution);
});
// RF-005: commit pending changes, push the worktree branch, open the PR; when
// the run is bound to a board issue the card moves to in_review and the PR
// link is commented on the issue (PipelineExecutionAppService).
runs.MapPost("{id}/create-pr", async (
        string id,
        CreatePrRequest request,
        IPipelineOrchestrator orchestrator,
        CancellationToken ct) =>
{
    var prUrl = await orchestrator.CreatePullRequestAsync(id, request.Title, request.Body, ct);
    return prUrl is null ? Results.NotFound() : Results.Created(prUrl, (object?)new { prUrl });
});

// SPEC-20260919-ade-observability-finops §5: summary agregado + telemetria por run.
var finops = api.MapGroup("harness/finops");
// SPEC-20260922-finops-dashboard-detail §5: invalid period → 400.
var finOpsPeriods = new HashSet<string>(StringComparer.Ordinal) { "24h", "last-7-days", "last-30-days", "all" };
finops.MapGet("summary", async (
        string? period,
        IFinOpsService finOps,
        CancellationToken ct) =>
    period is not null && !finOpsPeriods.Contains(period)
        ? Results.BadRequest(new { error = $"invalid period '{period}'" })
        : Results.Ok(await finOps.GetSummaryAsync(period ?? "last-30-days", ct)));
harness.MapGet("runs/{id}/telemetry", async (
        string id,
        IFinOpsService finOps,
        CancellationToken ct) =>
    await finOps.GetRunTelemetryAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

// SPEC-20260919-ade-living-specs §5: catálogo + drift das specs vivas.
// SPEC-20260920-global-repo-selector RF-005: ?repo=owner/name reads
// ~/repos/<name>/.specs; malformed repo → 400, missing clone → empty catalog.
var specs = api.MapGroup("specs");
specs.MapGet("", async Task<IResult> (
        string? status,
        string? q,
        string? repo,
        ISpecAppService svc,
        CancellationToken ct) =>
    !IsRepoShapeValid(repo)
        ? Results.BadRequest(new { error = "repo must have the 'owner/name' shape." })
        : Results.Ok(await svc.ListAsync(status, q, repo, ct)));
specs.MapGet("drift-report", async Task<IResult> (
        string? repo,
        ISpecDriftDetector detector,
        SpecDriftReportCache driftCache,
        CancellationToken ct) =>
    // Sem ?repo= serve o cache do scan horário (SPEC-20260920-harness-maintenance-jobs
    // RF-003) com fallback ao scan ao vivo antes do primeiro tick; com ?repo= faz o
    // scan live no clone selecionado (SPEC-20260920-global-repo-selector RF-005).
    !IsRepoShapeValid(repo)
        ? Results.BadRequest(new { error = "repo must have the 'owner/name' shape." })
        : Results.Ok(repo is null
            ? driftCache.Last ?? await detector.BuildReportAsync(null, ct)
            : await detector.BuildReportAsync(repo, ct)));
specs.MapGet("{id}", async Task<IResult> (
        string id,
        string? repo,
        ISpecAppService svc,
        CancellationToken ct) =>
    !IsRepoShapeValid(repo)
        ? Results.BadRequest(new { error = "repo must have the 'owner/name' shape." })
        : await svc.GetAsync(id, repo, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());
specs.MapPost("{id}/status", async Task<IResult> (
        string id,
        SpecStatusUpdateRequest request,
        string? repo,
        ISpecAppService svc,
        CancellationToken ct) =>
    !IsRepoShapeValid(repo)
        ? Results.BadRequest(new { error = "repo must have the 'owner/name' shape." })
        : await svc.UpdateStatusAsync(id, request.Status, repo, ct) is { } dto
            ? Results.Ok(dto)
            : Results.NotFound());

// SPEC-20260919-cli-metrics §5: ingestion status + aggregates for /agents.
var cliMetrics = api.MapGroup("local/cli-metrics");
cliMetrics.MapPost("sync", async (
        CliMetricsSyncCoordinator coordinator,
        CliMetricsOptions options,
        CancellationToken ct) =>
{
    if (!options.Enabled)
    {
        return Results.NotFound(new { error = "cli-metrics disabled" });
    }

    return Results.Ok(await coordinator.TrySyncAsync(ct));
});
cliMetrics.MapGet("sources", async (ICliMetricsService metrics, CancellationToken ct) =>
    Results.Ok(await metrics.GetSourcesAsync(ct)));
cliMetrics.MapGet("summary", async (string? period, ICliMetricsService metrics, CancellationToken ct) =>
    Results.Ok(await metrics.GetSummaryAsync(period, ct)));
cliMetrics.MapGet("sessions", async (
        AgentCliKind? kind, DateTime? from, DateTime? to, int? take,
        ICliMetricsService metrics, CancellationToken ct) =>
    Results.Ok(await metrics.GetSessionsAsync(kind, from, to, take ?? 100, ct)));

// RF-003: stateless Streamable HTTP MCP endpoint; inherits the group's
// RequireAuthorization (cookie or X-Api-Key).
api.MapMcp("mcp");

// SPEC-20260920 RF-005/RF-006: only 'owner/name' shapes are accepted — same
// charset the WASM selector enforces (RepositoryFilter.RepositoryNamePattern),
// so a name the client rejects is never silently sanitized server-side.
static bool IsRepoShapeValid(string? repo) =>
    string.IsNullOrEmpty(repo)
    || Regex.IsMatch(repo.Trim(), @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url))
    {
        return true;
    }

    return url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Contains("://", StringComparison.Ordinal);
}

api.MapPost("login", async (HttpContext context, AdminUser admin) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["Username"].ToString();
    var password = form["Password"].ToString();
    var rawReturnUrl = form["ReturnUrl"].ToString() ?? "/";
    var returnUrl = IsLocalUrl(rawReturnUrl) ? rawReturnUrl : "/";

    if (!string.Equals(admin.Username, username, StringComparison.Ordinal)
        || !admin.Validate(password))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Invalid credentials");
        return;
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.Role, "Admin")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true, RedirectUri = returnUrl });

    context.Response.Redirect(returnUrl);
}).DisableAntiforgery()
.RequireRateLimiting("login")
.AllowAnonymous();

api.MapPost("logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/login");
}).DisableAntiforgery();

api.MapPut("admin/password", (ChangePasswordRequest request, AdminUser admin) =>
{
    if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
    {
        return Results.BadRequest("Current and new passwords are required.");
    }

    if (!admin.Validate(request.CurrentPassword))
    {
        return Results.Unauthorized();
    }

    admin.ChangePassword(request.NewPassword);
    return Results.NoContent();
}).RequireAuthorization();

app.MapHealthChecks("/health").CacheOutput("ReadOnlyApi");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("data") || registration.Tags.Contains("db")
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

api.MapGet("meta", () => Results.Ok(new { name = "taskboard", version = "1.0.0", realtime = new { transport = "poll", intervalMs = 2000 } }))
   .CacheOutput("ReadOnlyApi")
   .AllowAnonymous();

api.MapGet("client-storage", () => Results.Ok(new { data = (string?)null }));
api.MapPut("client-storage", (object? _) => Results.NoContent());

api.MapGet("local/codex-thread-progress", () => Results.Ok(new { progress = (string?)null }));
api.MapGet("local/host-runtime", () => Results.Ok(new { runtime = "dotnet", version = Environment.Version.ToString() }));
api.MapGet("local/cloud-session", (CloudSessionService cloud) => Results.Ok(cloud.Get()));
api.MapPut("local/cloud-session", (UpdateCloudSessionRequest request, CloudSessionService cloud) =>
{
    var session = cloud.Update(request);
    return Results.Ok(session);
});
api.MapGet("local/jira-connection", (IJiraService jira) => Results.Ok(jira.GetConnection()));
api.MapPost("local/jira-connection", async (UpdateJiraConnectionRequest request, IJiraService jira, CancellationToken ct) =>
{
    jira.UpdateConnection(request);
    var connection = await jira.TestConnectionAsync(ct);
    return Results.Ok(connection);
});
api.MapPost("local/jira-connection/sync", async (IJiraService jira, CancellationToken ct) =>
{
    var result = await jira.SyncAsync(ct);
    return Results.Ok(result);
});
api.MapGet("local/ai/catalog", async (string? threadId, AiChatCatalogService catalog, CancellationToken ct) =>
    Results.Ok(new { models = await catalog.ListAsync(threadId, ct) }));
api.MapPost("local/ai/catalog", async (AiChatModelDto model, AiChatCatalogService catalog, CancellationToken ct) =>
{
    var error = await catalog.AddAsync(model, ct);
    return error switch
    {
        null => Results.Created($"/api/local/ai/catalog/{model.Id}", new { model }),
        "MODEL_EXISTS" => Results.Conflict(new { error = new { code = "MODEL_EXISTS", message = $"Model '{model.Id}' already exists." } }),
        "INVALID_AGENT" => Results.BadRequest(new { error = new { code = "INVALID_AGENT", message = $"Invalid agent type '{model.AgentType}'." } }),
        _ => Results.UnprocessableEntity(new { error = new { code = "agent-not-eligible", message = $"Agent '{model.AgentType}' is not installed, authenticated or enabled." } })
    };
});
api.MapGet("local/ai/composer/candidates", () => Results.Ok(new { candidates = Array.Empty<object>() }));
api.MapPost("local/ai/composer/rebind", (object? _) => Results.NoContent());

api.MapGet("local/ai/threads", async (AiChatService aiChatService, CancellationToken ct) =>
{
    var threads = await aiChatService.ListThreadsAsync(ct);
    return Results.Ok(new { threads });
});

api.MapPost("local/ai/threads", async (
    CreateAiChatThreadRequest request,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    var thread = await aiChatService.CreateThreadAsync(request, Actor.LocalUser(), ct);
    return Results.Created($"/api/local/ai/threads/{thread.Id}", new { thread });
});

// SPEC-20260922-ai-chat-command-bar RF-002: single-thread read backs the
// history popup refresh and thread reload.
api.MapGet("local/ai/threads/{id}", async (
    string id,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    var thread = await aiChatService.GetThreadAsync(AiChatThreadId.From(id), ct);
    return thread is null
        ? Results.NotFound(new { error = new { code = "THREAD_NOT_FOUND", message = $"Thread '{id}' not found." } })
        : Results.Ok(new { thread });
});

// RF-002: idempotent delete — removing an already-gone thread is the desired
// end state, so a stale row returns 204 instead of 404 noise.
api.MapDelete("local/ai/threads/{id}", async (
    string id,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    await aiChatService.DeleteThreadAsync(AiChatThreadId.From(id), ct);
    return Results.NoContent();
});

api.MapGet("local/ai/threads/{id}/events", async (HttpRequest request, HttpResponse response, string id, AiChatService aiChatService, IRepository<AiChatEvent> eventRepo, IThreadEventStreamService threadEvents, IConfiguration config, CancellationToken ct) =>
{
    var threadId = AiChatThreadId.From(id);

    // RF-002: unknown threads fail fast — both the JSON snapshot and the SSE
    // stream answer 404 instead of hanging on a stream that never produces.
    if (await aiChatService.GetThreadAsync(threadId, ct) is null)
    {
        return Results.NotFound(new { error = new { code = "THREAD_NOT_FOUND", message = $"Thread '{id}' not found." } });
    }

    var existing = await eventRepo.Query.Where(e => e.ThreadId == threadId).OrderBy(e => e.CreatedAt).Select(e => e.ToDto()).ToListAsync(ct);

    // SPEC-20260918-ai-chat-threads: JSON snapshot for plain REST consumers;
    // anything else gets the SSE stream (backlog replay + live events).
    if (request.Headers.Accept.Any(a => a is not null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Ok(new { events = existing });
    }

    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    // RF-001: idle streams die on proxy read timeouts (nginx default 60s) —
    // a heartbeat comment every SseHeartbeatSeconds keeps the connection
    // alive; a client disconnect ends the handler normally instead of
    // aborting mid-response (the 502s seen behind the reverse proxy).
    var heartbeatSeconds = Math.Max(1, config.GetValue("Taskboard:AiChat:SseHeartbeatSeconds", 15));
    var heartbeatInterval = TimeSpan.FromSeconds(heartbeatSeconds);

    try
    {
        foreach (var ev in existing)
        {
            await response.WriteAsync($"event: ai_chat.event\n", ct);
            await response.WriteAsync($"data: {JsonSerializer.Serialize(ev, ApiJsonOptions.Default)}\n\n", ct);
        }

        await response.Body.FlushAsync(ct);

        await using var enumerator = threadEvents.SubscribeAsync(id, ct).GetAsyncEnumerator(ct);
        // The pending MoveNext must survive heartbeat iterations — a second
        // MoveNextAsync while one is in flight is illegal on IAsyncEnumerable.
        Task<bool>? moveNext = null;
        while (true)
        {
            moveNext ??= enumerator.MoveNextAsync().AsTask();
            var heartbeat = Task.Delay(heartbeatInterval, ct);
            var completed = await Task.WhenAny(moveNext, heartbeat).ConfigureAwait(false);
            if (completed == heartbeat)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await response.WriteAsync(": hb\n\n", ct);
                await response.Body.FlushAsync(ct);
                continue;
            }

            if (!await moveNext.ConfigureAwait(false))
            {
                break;
            }

            moveNext = null;

            var live = enumerator.Current;
            await response.WriteAsync($"event: {live.Type}\n", ct);
            await response.WriteAsync($"data: {JsonSerializer.Serialize(live.Payload, ApiJsonOptions.Default)}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected (page nav, EventSource reconnect) — end the
        // response normally; throwing here aborts mid-response → upstream 502.
    }
    catch (IOException)
    {
        // Broken pipe on a gone client — same clean-close semantics.
    }

    return Results.Empty;
});

api.MapPost("local/ai/threads/{id}/events", async (
    string id,
    AddAiChatEventRequest request,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    var threadId = AiChatThreadId.From(id);
    var chatEvent = await aiChatService.AddEventAsync(threadId, request, Actor.LocalUser(), ct);
    return Results.Created($"/api/local/ai/threads/{id}/events/{chatEvent.Id}", new { aiChatEvent = chatEvent });
});

api.MapPost("local/ai/threads/{id}/prompt", async (
    string id,
    PromptAgentThreadRequest request,
    AgentSessionManager sessionManager,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var admitted = await sessionManager.PromptAsync(id, request.Text, request.Delivery ?? "queue", ct);
    return admitted
        ? Results.Accepted($"/api/local/ai/threads/{id}/prompt", new { admitted = true })
        : Results.Conflict(new { error = new { code = "THREAD_NOT_AGENT", message = "Thread is not configured for agent mode or session failed to start." } });
});

api.MapPost("local/ai/threads/{id}/queue", async (
    string id,
    PromptAgentThreadRequest request,
    AgentSessionManager sessionManager,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var queued = await sessionManager.EnqueuePromptAsync(id, request.Text, ct);
    return queued is not null
        ? Results.Created($"/api/local/ai/threads/{id}/queue/{queued.Id}", new { aiChatEvent = queued })
        : Results.Conflict(new { error = new { code = "THREAD_NOT_AGENT", message = "Thread is not configured for agent mode." } });
});

api.MapDelete("local/ai/threads/{id}/queue/{eventId}", async (
    string id,
    string eventId,
    AgentSessionManager sessionManager,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var removed = await sessionManager.CancelQueuedPromptAsync(id, eventId, ct);
    return removed
        ? Results.NoContent()
        : Results.NotFound(new { error = new { code = "QUEUED_PROMPT_NOT_FOUND", message = $"Queued prompt '{eventId}' not found." } });
});

api.MapPost("local/ai/threads/{id}/fork", async (
    string id,
    ForkAiChatThreadRequest request,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    var thread = await aiChatService.ForkThreadAsync(AiChatThreadId.From(id), request.EventId, Actor.LocalUser(), ct);
    return Results.Created($"/api/local/ai/threads/{thread.Id}", new { thread });
});

api.MapPost("local/ai/threads/{id}/retry", async (
    string id,
    AgentSessionManager sessionManager,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var admitted = await sessionManager.RetryLastPromptAsync(id, ct);
    return admitted
        ? Results.Accepted($"/api/local/ai/threads/{id}/retry", new { admitted = true })
        : Results.Conflict(new { error = new { code = "NO_USER_PROMPT", message = "No user prompt to retry or thread is not in agent mode." } });
});

api.MapPost("local/ai/threads/{id}/cancel", async (
    string id,
    AgentSessionManager sessionManager,
    IConfiguration config,
    CancellationToken ct) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var cancelled = await sessionManager.CancelAsync(id, ct);
    return cancelled
        ? Results.NoContent()
        : Results.Conflict(new { error = new { code = "NO_ACTIVE_RUN", message = "No active run to cancel." } });
});

api.MapPost("local/ai/threads/{id}/permissions/{requestId}/reply", (
    string id,
    string requestId,
    PermissionReplyRequest request,
    PermissionGate permissionGate,
    IConfiguration config) =>
{
    if (!config.GetValue<bool>("Taskboard:WebCliAgent:Enabled"))
    {
        return Results.NotFound(new { error = new { code = "FEATURE_DISABLED", message = "Web CLI Agent feature is disabled." } });
    }

    var ok = permissionGate.Reply(id, requestId, request.Outcome);
    return ok
        ? Results.NoContent()
        : Results.NotFound(new { error = new { code = "PERMISSION_EXPIRED", message = "Permission request expired or not found." } });
});

api.MapPost("local/ai/threads/{id}/runs", async (
    string id,
    AiChatService aiChatService,
    CancellationToken ct) =>
{
    var threadId = AiChatThreadId.From(id);
    var run = await aiChatService.StartRunAsync(threadId, Actor.LocalUser(), ct);
    return Results.Created($"/api/local/ai/threads/{id}/runs/{run.Id}", new { run });
});

api.MapPatch("local/ai/threads/{threadId}/runs/{runId}", async (
    string threadId,
    string runId,
    UpdateAiChatRunRequest request,
    IRepository<AiChatThread> threadRepo,
    IRepository<AiChatRun> runRepo,
    CancellationToken ct) =>
{
    var thread = await threadRepo.GetAsync(AiChatThreadId.From(threadId), ct);
    if (thread is null)
    {
        return Results.NotFound(new { error = new { code = "THREAD_NOT_FOUND", message = $"Thread '{threadId}' not found." } });
    }

    var run = await runRepo.GetAsync(AiChatRunId.From(runId), ct);
    if (run is null || run.ThreadId != thread.Id)
    {
        return Results.NotFound(new { error = new { code = "RUN_NOT_FOUND", message = $"Run '{runId}' not found." } });
    }

    if (request.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
    {
        run.Complete(request.ExitCode ?? 0);
    }
    else if (request.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
    {
        run.Fail(request.ExitCode);
    }
    else
    {
        return Results.BadRequest(new { error = new { code = "INVALID_RUN_STATUS", message = $"Status '{request.Status}' is not valid." } });
    }

    thread.SetStatus(run.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? AiChatThreadStatus.Idle : AiChatThreadStatus.Failed);
    await runRepo.UpdateAsync(run, ct);
    await threadRepo.UpdateAsync(thread, ct);
    await runRepo.SaveChangesAsync(ct);

    return Results.Ok(new { run = run.ToDto(), thread = thread.ToDto() });
});

app.MapGet("/api/events", async (HttpResponse response, IEventStreamService eventStream, CancellationToken ct) =>
{
    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    await foreach (var ev in eventStream.SubscribeAsync(ct))
    {
        await response.WriteAsync($"event: {ev.Type}\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(ev.Payload, ApiJsonOptions.Default)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}).RequireAuthorization();

// Static files are served by MapStaticAssets() below — no UseStaticFiles needed.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOutputCache();

app.UseAntiforgery();

// SPEC-20260915-blazor-wasm-migration: index.html + _framework assets are
// served anonymously; every API endpoint keeps RequireAuthorization and the
// WASM client routes itself to /login.
app.MapStaticAssets();
app.MapFrameworkAssetsApi();
app.MapHub<AgentLogHub>("/agent-log-hub").RequireAuthorization();
app.MapHub<TerminalHub>("/terminal-hub").RequireAuthorization();
// SPEC-20260919-ade-cockpit-hitl §5: stream de eventos estruturados por run.
app.MapHub<HarnessCockpitHub>("/harness-cockpit-hub").RequireAuthorization();

// SPEC-20260917-vscode-web-workspace RF-006: /vscode mount handling lives in
// middleware (not an endpoint) because endpoint routing ignores the trailing
// slash — MapGet("/vscode") would also match /vscode/ and redirect it to
// itself forever. Exact "/vscode" redirects to "/vscode/" (code-server needs
// the trailing slash for relative URLs); everything else is ensured-started
// then proxied; a friendly 503 beats a raw 502.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/vscode", StringComparison.OrdinalIgnoreCase),
    branch => branch.Use(async (ctx, next) =>
    {
        if (string.Equals(ctx.Request.Path.Value, "/vscode", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect($"/vscode/{ctx.Request.QueryString}");
            return;
        }

        var manager = ctx.RequestServices.GetRequiredService<ICodeServerManager>();
        var status = await manager.EnsureStartedAsync(ctx.RequestAborted);
        if (!status.Running)
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync(
                status.Installed
                    ? "code-server failed to start — check GET /api/vscode/status."
                    : "code-server is not installed — install it from the VS Code page.");
            return;
        }

        await next();
    }));

app.MapReverseProxy();

api.MapGet("settings", async (SettingsService settings, CancellationToken ct) =>
{
    var result = await settings.GetSettingsAsync(ct);
    return Results.Ok(new { settings = result });
});

api.MapPut("settings", async (SaveSettingsRequest request, SettingsService settings, CancellationToken ct) =>
{
    await settings.SaveSettingsAsync(request, ct);
    return Results.NoContent();
}).RequireAuthorization();

api.MapGet("configuration", (RuntimeConfigurationService configuration) =>
    Results.Ok(new { entries = configuration.GetEntries() }))
    .RequireAuthorization();

api.MapPut("configuration/{key}", async (
    string key,
    SetConfigurationRequest request,
    RuntimeConfigurationService configuration,
    SqliteConfigurationProvider overridesProvider,
    CancellationToken ct) =>
{
    var result = await configuration.SetOverrideAsync(key, request.Value, ct);
    if (result.Error is not ConfigurationWriteError.None)
    {
        return ConfigurationError(result);
    }

    overridesProvider.Reload();
    return Results.NoContent();
}).RequireAuthorization();

api.MapDelete("configuration/{key}", async (
    string key,
    RuntimeConfigurationService configuration,
    SqliteConfigurationProvider overridesProvider,
    CancellationToken ct) =>
{
    var result = await configuration.DeleteOverrideAsync(key, ct);
    if (result.Error is not ConfigurationWriteError.None)
    {
        return ConfigurationError(result);
    }

    overridesProvider.Reload();
    return Results.NoContent();
}).RequireAuthorization();

static IResult ConfigurationError(ConfigurationWriteResult result) => result.Error switch
{
    ConfigurationWriteError.ReadOnly => Results.BadRequest(new
    {
        error = new { code = "KEY_READ_ONLY", message = result.Message }
    }),
    ConfigurationWriteError.NotFound => Results.NotFound(new
    {
        error = new { code = "OVERRIDE_NOT_FOUND", message = result.Message }
    }),
    _ => Results.BadRequest(new
    {
        error = new { code = "VALIDATION", message = result.Message }
    })
};

api.MapGet("skills", async (ISkillDiscoveryService skills, CancellationToken ct) =>
{
    var result = await skills.DiscoverAsync(ct);
    return Results.Ok(new { skills = result });
});

api.MapGet("skills/{source}/{name}", async (string source, string name, ISkillDiscoveryService skills, CancellationToken ct) =>
{
    var result = await skills.GetDetailAsync(source, name, ct);
    return result is null ? Results.NotFound() : Results.Ok(new { skill = result });
});

api.MapGet("skills/{source}/{name}/files/{**path}", async (string source, string name, string path, ISkillDiscoveryService skills, CancellationToken ct) =>
{
    var result = await skills.GetFileAsync(source, name, path, ct);
    return result.Error switch
    {
        SkillFileError.None => Results.Ok(new { path = result.RelativePath, content = result.Content }),
        SkillFileError.InvalidPath => Results.BadRequest(new { error = new { code = "INVALID_PATH", message = "File path is not valid." } }),
        SkillFileError.TooLarge => Results.Json(new { error = new { code = "FILE_TOO_LARGE", message = "File exceeds the 256 KB limit." } }, statusCode: StatusCodes.Status413PayloadTooLarge),
        SkillFileError.NotText => Results.Json(new { error = new { code = "NOT_TEXT", message = "File is not a supported text file." } }, statusCode: StatusCodes.Status415UnsupportedMediaType),
        _ => Results.NotFound(new { error = new { code = "FILE_NOT_FOUND", message = "Skill or file not found." } })
    };
});

api.MapGet("auth/me", (HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new { authenticated = true, username = context.User.Identity.Name })
        : Results.Unauthorized()).AllowAnonymous();

var github = api.MapGroup("github").RequireAuthorization();

github.MapGet("repositories", async (IGitHubService gitHub, CancellationToken ct) =>
{
    try
    {
        var repositories = await gitHub.GetRepositoriesAsync(ct);
        return Results.Ok(new { repositories });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = new { code = "GITHUB_TOKEN_MISSING", message = ex.Message } });
    }
});

github.MapGet("repos/{owner}/{repo}/issues", async (string owner, string repo, IGitHubService gitHub, CancellationToken ct) =>
{
    try
    {
        var issues = await gitHub.GetIssuesAsync($"{owner}/{repo}", ct);
        return Results.Ok(new { issues });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = new { code = "GITHUB_TOKEN_MISSING", message = ex.Message } });
    }
});

github.MapGet("repos/{owner}/{repo}/issues/{number:int}", async (
    string owner,
    string repo,
    int number,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    try
    {
        var issue = await gitHub.GetIssueAsync($"{owner}/{repo}", number, ct);
        return issue is null ? Results.NotFound() : Results.Ok(new { issue });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = new { code = "GITHUB_TOKEN_MISSING", message = ex.Message } });
    }
});

github.MapPost("repos/{owner}/{repo}/issues", async (
    string owner,
    string repo,
    CreateGitHubIssueRequest request,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    var issue = await gitHub.CreateIssueAsync(
        $"{owner}/{repo}", request.Title, request.Body, request.InitialColumn, ct);
    return Results.Ok(new { issue });
});

github.MapPut("repos/{owner}/{repo}/issues/{number:int}/column", async (
    string owner,
    string repo,
    int number,
    UpdateGitHubIssueColumnRequest request,
    IGitHubService gitHub,
    IRepository<IssueHistoryEvent> history,
    CancellationToken ct) =>
{
    var issue = await gitHub.UpdateIssueColumnAsync(
        $"{owner}/{repo}", number, request.OldColumn, request.NewColumn, ct);
    await RecordIssueHistoryAsync(history, issue, $"{owner}/{repo}", IssueHistoryEventKind.ColumnMoved, ct,
        from: request.OldColumn?.ToLabel(), to: request.NewColumn.ToLabel());
    return Results.Ok(new { issue });
});

github.MapPost("repos/{owner}/{repo}/issues/{number:int}/labels", async (
    string owner,
    string repo,
    int number,
    AddGitHubLabelsRequest request,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    await gitHub.AddLabelsToIssueAsync($"{owner}/{repo}", number, request.Labels, ct);
    return Results.NoContent();
});

// SPEC-20260918-kanban-card-ux: edit body/title, swap priority labels, close as canceled/archived.
github.MapPatch("repos/{owner}/{repo}/issues/{number:int}", async (
    string owner,
    string repo,
    int number,
    UpdateGitHubIssueRequest request,
    IGitHubService gitHub,
    IRepository<IssueHistoryEvent> history,
    CancellationToken ct) =>
{
    try
    {
        var issue = await gitHub.UpdateIssueAsync(
            $"{owner}/{repo}", number, request.Title, request.Body, ct);
        var changed = string.Join(", ", new[]
            {
                request.Title is not null ? "title" : null,
                request.Body is not null ? "body" : null
            }.Where(x => x is not null));
        await RecordIssueHistoryAsync(history, issue, $"{owner}/{repo}", IssueHistoryEventKind.Edited, ct,
            detail: string.IsNullOrEmpty(changed) ? null : changed);
        return Results.Ok(new { issue });
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "issue-not-found" });
    }
});

github.MapPut("repos/{owner}/{repo}/issues/{number:int}/priority", async (
    string owner,
    string repo,
    int number,
    SetIssuePriorityRequest request,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    if (GitHubBoardColumnExtensions.NormalizePriority(request.Priority) is null)
    {
        return Results.BadRequest(new { error = "invalid-priority" });
    }

    try
    {
        var issue = await gitHub.SetIssuePriorityAsync(
            $"{owner}/{repo}", number, request.Priority, ct);
        return Results.Ok(new { issue });
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "issue-not-found" });
    }
});

github.MapPost("repos/{owner}/{repo}/issues/{number:int}/close", async (
    string owner,
    string repo,
    int number,
    CloseGitHubIssueRequest request,
    IGitHubService gitHub,
    IRepository<IssueHistoryEvent> history,
    CancellationToken ct) =>
{
    if (!string.Equals(request.Resolution, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(request.Resolution, "archived", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "invalid-resolution" });
    }

    try
    {
        var issue = await gitHub.CloseIssueAsync(
            $"{owner}/{repo}", number, request.Resolution, ct);
        await RecordIssueHistoryAsync(history, issue, $"{owner}/{repo}", IssueHistoryEventKind.Closed, ct,
            detail: request.Resolution);
        return Results.Ok(new { issue });
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "issue-not-found" });
    }
});

// SPEC-20260918-github-comments-history RF-001: issue comments are the
// handoff channel between agents/humans — list chronological, create posts
// straight to GitHub (never persisted locally; GitHub is the source of truth).
github.MapGet("repos/{owner}/{repo}/issues/{number:int}/comments", async (
    string owner,
    string repo,
    int number,
    int? take,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    var comments = await gitHub.GetIssueCommentsAsync($"{owner}/{repo}", number, take ?? 50, ct);
    return Results.Ok(new { comments });
});

github.MapPost("repos/{owner}/{repo}/issues/{number:int}/comments", async (
    string owner,
    string repo,
    int number,
    AddIssueCommentRequest request,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Body))
    {
        return Results.BadRequest(new { error = "empty-body" });
    }

    try
    {
        var comment = await gitHub.AddIssueCommentAsync($"{owner}/{repo}", number, request.Body, ct);
        return Results.Ok(new { comment });
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "issue-not-found" });
    }
});

// SPEC-20260919-ade-cockpit-hitl RF-005: criação de PR via Octokit (token fica
// server-side; o WASM client chama este endpoint via HttpGitHubService).
github.MapPost("repos/{owner}/{repo}/pulls", async (
    string owner,
    string repo,
    CreatePullRequestBody request,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    var prUrl = await gitHub.CreatePullRequestAsync(
        $"{owner}/{repo}", request.Title, request.Head, request.BaseBranch, request.Body, ct);
    return Results.Created(prUrl, new { prUrl });
});

// Unified issue timeline: persisted board events + agent runs, newest first.
github.MapGet("issues/{issueId}/history", async (
    string issueId,
    int? take,
    IRepository<IssueHistoryEvent> history,
    IAgentRunRepository runs,
    CancellationToken ct) =>
{
    var events = await history.Query
        .Where(e => e.IssueId == issueId)
        .OrderByDescending(e => e.OccurredAt)
        .Take(take ?? 50)
        .ToListAsync(ct);

    var agentRuns = await runs.GetByIssueIdAsync(issueId, take ?? 50, ct);

    var items = events
        .Select(e => new IssueHistoryItemDto(
            e.Kind switch
            {
                IssueHistoryEventKind.ColumnMoved => IssueHistoryItemDto.ColumnMoved,
                IssueHistoryEventKind.Edited => IssueHistoryItemDto.Edited,
                IssueHistoryEventKind.Closed => IssueHistoryItemDto.Closed,
                IssueHistoryEventKind.RunStarted => IssueHistoryItemDto.PipelineRun,
                _ => "event"
            },
            e.OccurredAt,
            From: e.From,
            To: e.To,
            Detail: e.Detail))
        .Concat(agentRuns.Select(r => new IssueHistoryItemDto(
            IssueHistoryItemDto.AgentRun,
            r.StartedAt,
            AgentType: r.AgentType,
            AgentRunState: r.State,
            FinishedAt: r.FinishedAt)))
        .OrderByDescending(i => i.OccurredAt)
        .Take(take ?? 50)
        .ToList();

    return Results.Ok(new { items });
});

// SPEC-20260918-gantt-github-timeline: Gantt data + kanban flow metrics.
github.MapGet("repos/{owner}/{repo}/timeline", async (
    string owner,
    string repo,
    int? days,
    ITimelineMetricsService metrics,
    CancellationToken ct) =>
{
    try
    {
        var timeline = await metrics.GetTimelineAsync(owner, repo, days ?? 90, ct);
        return Results.Ok(timeline);
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "repo-not-found" });
    }
});

github.MapGet("repos/{owner}/{repo}/metrics", async (
    string owner,
    string repo,
    int? days,
    ITimelineMetricsService metrics,
    CancellationToken ct) =>
{
    try
    {
        var result = await metrics.GetMetricsAsync(owner, repo, days ?? 90, ct);
        return Results.Ok(result);
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "repo-not-found" });
    }
});

github.MapGet("repos/{owner}/{repo}/workflows", async (
    string owner,
    string repo,
    IGitHubService gitHub,
    IConfiguration config,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    // SPEC-20260922-workflow-actions-resilience RF-002 — explicit deadline:
    // the page must degrade, not hang until the client's ~100s HTTP timeout.
    var deadlineSeconds = Math.Max(1, config.GetValue("Taskboard:GitHub:WorkflowsDeadlineSeconds", 25));
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
    deadline.CancelAfter(TimeSpan.FromSeconds(deadlineSeconds));

    try
    {
        var monitorTask = gitHub.GetWorkflowsAsync($"{owner}/{repo}", deadline.Token);
        var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
        if (await Task.WhenAny(monitorTask, timeoutTask) != monitorTask)
        {
            _ = monitorTask.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            loggerFactory.CreateLogger("GitHub.Workflows").LogWarning(
                "Workflows deadline of {Seconds}s exceeded for {Owner}/{Repo}",
                deadlineSeconds, owner, repo);
            return Results.Ok(new WorkflowMonitorDto([], [], true));
        }

        return Results.Ok(await monitorTask);
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "repo-not-found" });
    }
});

github.MapGet("repos/{owner}/{repo}/workflows/{workflowId:long}/runs", async (
    string owner,
    string repo,
    long workflowId,
    int? take,
    IGitHubService gitHub,
    CancellationToken ct) =>
{
    try
    {
        var runs = await gitHub.GetWorkflowRunsAsync($"{owner}/{repo}", workflowId, take ?? 10, ct);
        return Results.Ok(new { runs });
    }
    catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "repo-not-found" });
    }
});

var agents = api.MapGroup("agents").RequireAuthorization();

agents.MapGet("", async (IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    var available = await orchestration.GetAvailableAgentsAsync(ct);
    return Results.Ok(new { agents = available });
});

agents.MapPost("executions", async (
    AgentExecutionRequest request,
    IAgentOrchestrationService orchestration,
    CancellationToken ct) =>
{
    // SPEC-20260918-agent-model-config RF-001: fail fast on a malformed repo
    // slug instead of burning an agent run and failing at move-to-review.
    if (!IsGitHubRepoFullName(request.RepositoryFullName))
    {
        return Results.BadRequest(new { error = "invalid-repository" });
    }

    var queued = await orchestration.EnqueueAsync(request, ct);
    return queued
        ? Results.Accepted()
        : Results.UnprocessableEntity(new { error = "agent-not-eligible", agentType = request.AgentType.ToString() });
});

// SPEC-20260918-agent-model-config: per-CLI tier model configuration.
agents.MapGet("{agentType}/models", async (
    AgentType agentType,
    IAgentModelConfigService modelConfig,
    CancellationToken ct) =>
{
    var config = await modelConfig.GetConfigAsync(agentType, ct);
    return config.SupportsModelSelection
        ? Results.Ok(config)
        : Results.UnprocessableEntity(new { error = "model-selection-unsupported", agentType = agentType.ToString() });
});

agents.MapPut("{agentType}/models", async (
    AgentType agentType,
    SaveAgentModelConfigRequest body,
    IAgentModelConfigService modelConfig,
    CancellationToken ct) =>
{
    try
    {
        await modelConfig.SetConfigAsync(agentType, body, ct);
        return Results.Ok(await modelConfig.GetConfigAsync(agentType, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "invalid-model", message = ex.Message });
    }
});

// Live model catalog reported by the installed CLI itself (e.g.
// `opencode models`, `devin models list`, `agy models`) — feeds the editable
// dropdown in the Models dialog. Empty when the CLI has no probe or is not
// installed; 422 for CLI-managed agents.
agents.MapGet("{agentType}/models/available", async (
    AgentType agentType,
    IAgentModelCatalogService modelCatalog,
    CancellationToken ct,
    bool refresh = false) =>
{
    if (!AgentCliModels.SupportsModelSelection(agentType))
    {
        return Results.UnprocessableEntity(new { error = "model-selection-unsupported", agentType = agentType.ToString() });
    }

    return Results.Ok(new AvailableAgentModelsResponse(await modelCatalog.ListAvailableAsync(agentType, refresh, ct)));
});

agents.MapDelete("{agentType}/models", async (
    AgentType agentType,
    IAgentModelConfigService modelConfig,
    CancellationToken ct) =>
{
    await modelConfig.DeleteConfigAsync(agentType, ct);
    return Results.Ok(await modelConfig.GetConfigAsync(agentType, ct));
});

agents.MapGet("runs", async (
    string issueId,
    int? take,
    IAgentOrchestrationService orchestration,
    CancellationToken ct) =>
{
    var runs = await orchestration.GetRunsAsync(issueId, take ?? 5, ct);
    return Results.Ok(new { runs });
});

agents.MapGet("runs/active", async (IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    var runs = await orchestration.GetLatestRunsAsync(ct);
    return Results.Ok(new { runs });
});

agents.MapGet("logs/{issueId}", async (string issueId, IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    var logs = await orchestration.GetLogsAsync(issueId, ct);
    return Results.Ok(new { logs });
});

agents.MapDelete("logs/{issueId}", async (string issueId, IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.ClearLogsAsync(issueId, ct);
    return Results.NoContent();
});

// SPEC-20260921-agent-execution-event-pipeline RF-003: paginated replay of
// normalized events per scope (run | thread | issue).
agents.MapGet("events", async (
    string scopeKind,
    string scopeId,
    IAgentExecutionEventSink sink,
    long? after,
    int? take,
    CancellationToken ct) =>
{
    if (scopeKind is not ("run" or "thread" or "issue") || string.IsNullOrWhiteSpace(scopeId))
    {
        return Results.BadRequest(new { error = "scopeKind must be run|thread|issue and scopeId is required." });
    }

    // Fetch one extra row so hasMore is exact — a full page alone does not
    // imply another page exists.
    var pageSize = take ?? 500;
    var fetched = await sink.GetEventsAsync(scopeKind, scopeId, after ?? 0, pageSize + 1, ct);
    var hasMore = fetched.Count > pageSize;
    var events = hasMore ? fetched.Take(pageSize).ToList() : fetched;
    var nextAfter = events.Count > 0 ? events[^1].Sequence : after ?? 0;
    return Results.Ok(new { events, nextAfter, hasMore });
});

agents.MapPost("executions/{issueId}/cancel", async (string issueId, IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.CancelAsync(issueId, ct);
    return Results.NoContent();
});

// SPEC-20260921-board-cockpit-agent-observability RF-004: superfície de
// controle unificada — AgentControlService roteia por escopo e audita a
// ação como evento normalizado.
agents.MapPost("control", async (
    AgentControlRequest request,
    AgentControlService control,
    CancellationToken ct) =>
    MapControlResult(await control.ExecuteAsync(request, ct)));

// Reply unificado de permissões: thread → PermissionGate da sessão ACP;
// run → gate de stage do pipeline (requestId "stage:<key>").
agents.MapPost("permissions/reply", async (
    AgentPermissionReplyRequest request,
    AgentControlService control,
    CancellationToken ct) =>
    MapControlResult(await control.ReplyPermissionAsync(request, ct)));

// Snapshot de estado por escopo para montagem da UI (reconnect/poll).
agents.MapGet("state", async (
    string scopeKind,
    string scopeId,
    AgentControlService control,
    CancellationToken ct) =>
    MapControlResult(await control.GetScopeStateAsync(scopeKind, scopeId, ct)));

static IResult MapControlResult(AgentControlResult result) => result.Status switch
{
    AgentControlStatus.Accepted => Results.Accepted(),
    AgentControlStatus.Ok => Results.Ok(result.Payload),
    AgentControlStatus.Conflict => Results.Conflict(new { error = result.Error }),
    AgentControlStatus.NotFound => Results.NotFound(new { error = result.Error }),
    AgentControlStatus.Gone => Results.Json(new { error = result.Error }, statusCode: 410),
    _ => Results.BadRequest(new { error = result.Error }),
};

agents.MapGet("prompt-template", (IConfiguration configuration) =>
{
    var configured = configuration[AgentPromptTemplate.ConfigurationKey];
    return Results.Ok(new
    {
        template = string.IsNullOrWhiteSpace(configured) ? AgentPromptTemplate.Builtin : configured,
        builtin = AgentPromptTemplate.Builtin,
        customized = !string.IsNullOrWhiteSpace(configured) && configured != AgentPromptTemplate.Builtin
    });
});

agents.MapPut("prompt-template", async (
    PromptTemplateRequest request,
    RuntimeConfigurationService configuration,
    SqliteConfigurationProvider overridesProvider,
    CancellationToken ct) =>
{
    if (request.Template is { Length: > AgentPromptTemplate.MaxLength })
    {
        return Results.BadRequest(new { error = "template-too-long", max = AgentPromptTemplate.MaxLength });
    }

    var result = await configuration.SetOverrideAsync(
        AgentPromptTemplate.ConfigurationKey, request.Template ?? string.Empty, ct);
    if (result.Error is not ConfigurationWriteError.None)
    {
        return ConfigurationError(result);
    }

    overridesProvider.Reload();
    return Results.NoContent();
});

api.MapGet("skills/sync/status", (ISkillsSyncService sync) =>
    Results.Ok(sync.GetStatus()))
    .RequireAuthorization();

api.MapPost("skills/sync", async (
    ISkillsSyncService sync,
    IServiceScopeFactory scopeFactory,
    CancellationToken ct) =>
{
    var agents = await EnabledAgentResolver.ResolveAsync(scopeFactory, ct);
    sync.RequestSync(agents);
    return Results.Json(sync.GetStatus(), statusCode: StatusCodes.Status202Accepted);
}).RequireAuthorization();

api.MapGet("skills/install/status", (ISkillsInstallerService installer) =>
    Results.Ok(installer.GetStatus()))
    .RequireAuthorization();

api.MapPost("skills/install", (ISkillsInstallerService installer) =>
{
    installer.RequestInstall();
    return Results.Json(installer.GetStatus(), statusCode: StatusCodes.Status202Accepted);
}).RequireAuthorization();

api.MapPost("skills/install/verify", async (
    ISkillsInstallerService installer,
    CancellationToken ct) =>
    Results.Ok(await installer.VerifyAsync(ct)))
    .RequireAuthorization();

// SPEC-20260918-agent-execution-ux RF-006: process log of install/sync runs.
api.MapGet("skills/log", (SkillsOperationLog log) =>
    Results.Ok(log.Snapshot()))
    .RequireAuthorization();

api.MapGet("agent-clis", async (IAgentCliStatusService agentClis, CancellationToken ct) =>
    Results.Ok(await agentClis.GetStatusAsync(ct)))
    .RequireAuthorization();

// SPEC-20260918-cli-agents-expansion RF-004/RF-005: managed install runs.
api.MapPost("agent-clis/{kind}/install", async (
    string kind,
    IAgentCliInstallService installs,
    CancellationToken ct) =>
{
    if (!Enum.TryParse<AgentCliKind>(kind, ignoreCase: true, out var parsed)
        || AgentCliMap.GetSpec(parsed) is null)
    {
        return Results.NotFound();
    }

    var status = await installs.StartInstallAsync(parsed, ct);
    return status.State == AgentCliInstallState.Running
        ? Results.Json(status, statusCode: StatusCodes.Status202Accepted)
        : Results.Ok(status);
}).RequireAuthorization();

api.MapGet("agent-clis/{kind}/install/status", (
    string kind,
    IAgentCliInstallService installs) =>
{
    if (!Enum.TryParse<AgentCliKind>(kind, ignoreCase: true, out var parsed)
        || AgentCliMap.GetSpec(parsed) is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(installs.GetStatus(parsed));
}).RequireAuthorization();

// SPEC-20260917-vscode-web-workspace RF-004/RF-006/RF-008: editor status, managed
// install and per-card workdir resolution.
api.MapGet("vscode/status", async (ICodeServerManager manager, CancellationToken ct) =>
    Results.Ok(await manager.GetStatusAsync(ct)))
    .RequireAuthorization();

api.MapPost("vscode/install", async (IVscodeInstallService installs, CancellationToken ct) =>
{
    var status = await installs.StartInstallAsync(ct);
    return status.State == AgentCliInstallState.Running
        ? Results.Json(status, statusCode: StatusCodes.Status202Accepted)
        : Results.Ok(status);
}).RequireAuthorization();

api.MapGet("vscode/install/status", (IVscodeInstallService installs) =>
    Results.Ok(installs.GetStatus()))
    .RequireAuthorization();

// SPEC-20260920-global-repo-selector RF-008: restart a wedged code-server —
// kill → spawn → wait-listening inside the manager (single-flight), not an app
// restart. 404 when code-server is not installed, 503 when it is not listening
// after the restart — a bare 200 would make the UI reload a dead editor.
api.MapPost("vscode/restart", async Task<IResult> (ICodeServerManager manager, CancellationToken ct) =>
{
    var status = await manager.RestartAsync(ct);
    return status switch
    {
        { Installed: false } => Results.NotFound(status),
        { Running: false } => Results.Json(status, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Ok(status),
    };
}).RequireAuthorization();

api.MapGet("vscode/workdir", (string repo, WorkspaceService workspace) =>
{
    // Only 'owner/name' shapes resolve — anything else is a 404 rather than a
    // surprising path under the root.
    var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
    {
        return Results.NotFound();
    }

    var path = workspace.ResolveCardWorkdir(repo, out var exists);
    return Results.Ok(new VscodeWorkdir(path, exists));
}).RequireAuthorization();

// Direct-to-editor deep link: resolves the card workdir and redirects into
// the proxied code-server UI, skipping the /editor wrapper page entirely.
api.MapGet("vscode/open", (string repo, WorkspaceService workspace) =>
{
    var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
    {
        return Results.NotFound();
    }

    var path = workspace.ResolveCardWorkdir(repo, out _);
    return Results.Redirect($"/vscode/?folder={Uri.EscapeDataString(path)}");
}).RequireAuthorization();

api.MapGet("mcp/status", (IMcpProvisioningService mcp) =>
    Results.Ok(mcp.GetStatus()))
    .RequireAuthorization();

api.MapPost("mcp/sync", IResult (
    IMcpProvisioningService mcp,
    SqliteConfigurationProvider overridesProvider) =>
{
    // Reload DB overrides first so a URL written outside PUT mcp/rag is honored.
    overridesProvider.Reload();
    if (string.IsNullOrWhiteSpace(mcp.GetStatus().ConfiguredUrl))
    {
        // SPEC-20260918-rag-mcp-sync RF-001: Sync never silently removes —
        // un-provisioning is the explicit POST mcp/remove action.
        return Results.BadRequest(new
        {
            error = new
            {
                code = "rag-not-configured",
                message = "No RAG MCP URL saved — save it first with 'Save & Sync' (removal is the explicit Remove action)."
            }
        });
    }

    mcp.RequestProvision();
    return Results.Json(mcp.GetStatus(), statusCode: StatusCodes.Status202Accepted);
}).RequireAuthorization();

api.MapPost("mcp/remove", (
    IMcpProvisioningService mcp,
    SqliteConfigurationProvider overridesProvider) =>
{
    overridesProvider.Reload();
    mcp.RequestRemoval();
    return Results.Json(mcp.GetStatus(), statusCode: StatusCodes.Status202Accepted);
}).RequireAuthorization();

// SPEC-20260918-agent-execution-ux RF-007: process log of provisioning runs.
api.MapGet("mcp/log", (McpOperationLog log) =>
    Results.Ok(log.Snapshot()))
    .RequireAuthorization();

api.MapPut("mcp/rag", async (
    SaveRagMcpRequest request,
    RuntimeConfigurationService configuration,
    SqliteConfigurationProvider overridesProvider,
    IMcpProvisioningService mcp,
    CancellationToken ct) =>
{
    // null = keep the stored value; "" clears it — except URL: clearing used to
    // mean "remove everywhere", which is now the explicit POST mcp/remove action.
    if (request.Url is not null && string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new
        {
            error = new
            {
                code = "rag-url-required",
                message = "URL cannot be empty — to un-provision the MCP server use the Remove action."
            }
        });
    }

    var writes = new List<(string Key, string Value)>();
    if (request.Name is not null)
    {
        writes.Add(("Taskboard:Rag:ServerName", request.Name));
    }

    if (request.Url is not null)
    {
        writes.Add(("Taskboard:Rag:Url", request.Url));
    }

    if (request.ApiKey is not null)
    {
        writes.Add(("Taskboard:Rag:ApiKey", request.ApiKey));
    }

    foreach (var (key, value) in writes)
    {
        var result = await configuration.SetOverrideAsync(key, value, ct);
        if (result.Error is not ConfigurationWriteError.None)
        {
            return ConfigurationError(result);
        }
    }

    overridesProvider.Reload();
    mcp.RequestProvision();
    return Results.NoContent();
}).RequireAuthorization();

app.MapSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Harness v1");
    options.DocumentTitle = "Harness - API";
    options.RoutePrefix = "swagger";
});

// Unmatched /api/* must not fall through to the SPA fallback — API clients
// deserve a real 404 instead of an index.html 200.
api.MapMethods("{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], () => Results.NotFound());

app.MapFallbackToFile("index.html");

app.Run();

static async System.Threading.Tasks.Task<IReadOnlyCollection<AgentType>> ResolveEnabledAgentsAsync(
    IServiceProvider sp,
    CancellationToken ct)
{
    await using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
    var preferences = await scope.ServiceProvider
        .GetRequiredService<IRepository<AgentPreference>>()
        .ListAsync(ct);
    return preferences.Count == 0
        ? (IReadOnlyCollection<AgentType>)Enum.GetValues<AgentType>()
        : preferences.Where(p => p.Enabled).Select(p => p.AgentType).ToList();
}

static async System.Threading.Tasks.Task<string?> ResolveGitHubTokenAsync(
    IServiceProvider sp,
    CancellationToken ct)
{
    var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrWhiteSpace(envToken))
    {
        return envToken.Trim();
    }

    await using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
    var users = await scope.ServiceProvider
        .GetRequiredService<IRepository<UserPreference>>()
        .ListAsync(ct);
    return users.FirstOrDefault()?.GitHubToken;
}

// Persists a board-side event on a GitHub issue (column move, edit, close)
// so the issue's History tab can show what happened alongside agent runs.
// Best-effort: a history write failure must not fail the mutation itself.
static async System.Threading.Tasks.Task RecordIssueHistoryAsync(
    IRepository<IssueHistoryEvent> history,
    IssueDto issue,
    string repository,
    IssueHistoryEventKind kind,
    CancellationToken ct,
    string? from = null,
    string? to = null,
    string? detail = null) =>
    await RecordIssueHistoryByIdAsync(
        history,
        issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        repository, kind, ct, from, to, detail);

static async System.Threading.Tasks.Task RecordIssueHistoryByIdAsync(
    IRepository<IssueHistoryEvent> history,
    string issueId,
    string repository,
    IssueHistoryEventKind kind,
    CancellationToken ct,
    string? from = null,
    string? to = null,
    string? detail = null)
{
    try
    {
        await history.AddAsync(
            new IssueHistoryEvent(
                Guid.NewGuid(),
                issueId,
                repository,
                kind,
                DateTimeOffset.UtcNow,
                from,
                to,
                detail),
            ct);
        await history.SaveChangesAsync(ct);
    }
    catch
    {
        // History is auxiliary — never break the request over it.
    }
}

static bool IsGitHubRepoFullName(string? value) =>
    value is not null
    && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
