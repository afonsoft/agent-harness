using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.EntityFrameworkCore;
using Taskboard;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Events;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore;
using Taskboard.EntityFrameworkCore.Agents;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Application.Configuration;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Configuration;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.GitHub;
using Taskboard.Integrations.Jira;
using Taskboard.Integrations.Skills;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Skills;
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
using Taskboard.ValueObjects;
using DomainTask = Taskboard.Domain.Entities.Task;
using TaskStatus = Taskboard.ValueObjects.TaskStatus;

var builder = WebApplication.CreateBuilder(args);

var environment = new TaskboardEnvironment(builder.Configuration, builder.Environment);
var dataDir = environment.GetDataDir();
Directory.CreateDirectory(dataDir);

// Database-stored overrides are registered last so they win over env vars and
// appsettings. Loaded now so a Taskboard:Port override applies to this boot.
var sqliteConfigProvider = new SqliteConfigurationProvider(Path.Combine(dataDir, "taskboard.sqlite"));
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
builder.Services.AddSingleton<CloudSessionService>();
builder.Services.AddSingleton<WorkflowCapabilityService>();
builder.Services.AddSingleton<IJiraService, JiraService>();
builder.Services.AddSingleton<IExecutableResolver, CodexExecutableResolver>();
builder.Services.AddSingleton<IProcessTreeSignaler, ProcessTreeSignaler>();

// AI Chat services
builder.Services.AddSingleton<ILLMProvider, MockLLMProvider>();
builder.Services.AddScoped<AiChatService>();

builder.Services.AddHttpClient<JiraService>();

builder.Services.AddSignalR();
builder.Services.AddAntiforgery();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Taskboard AI API",
        Version = "v1",
        Description = "API for taskboard-ai projects, tasks, agents and skills."
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
builder.Services.AddSingleton<IAgentAcpClient, JsonRpcAcpClient>();
builder.Services.AddSingleton<IAgentAdapter, KnownCliAgentAdapter>();
builder.Services.AddSingleton<IAgentLogBroadcaster, SignalRAgentLogBroadcaster>();
builder.Services.AddScoped<IAgentLogRepository, EfCoreAgentLogRepository>();
builder.Services.AddSingleton<IAgentOrchestrationService, AgentOrchestrationService>();
builder.Services.AddHostedService(sp => (AgentOrchestrationService)sp.GetRequiredService<IAgentOrchestrationService>());

builder.Services.AddSingleton<ISkillsSyncService>(sp => new SkillsSyncService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<SkillsSyncService>>(),
    Path.Join(dataDir, "skills-cache"),
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    async ct =>
    {
        await using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var preferences = await scope.ServiceProvider
            .GetRequiredService<IRepository<AgentPreference>>()
            .ListAsync(ct);
        return preferences.Count == 0
            ? (IReadOnlyCollection<AgentType>)Enum.GetValues<AgentType>()
            : preferences.Where(p => p.Enabled).Select(p => p.AgentType).ToList();
    },
    async ct =>
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
    }));

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

var connectionString = $"Data Source={Path.Combine(dataDir, "taskboard.sqlite")}";

builder.Services.AddTaskboardEntityFrameworkCore(connectionString);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new StringIdJsonConverterFactory());
    options.SerializerOptions.Converters.Add(new StringValueObjectJsonConverterFactory());
});

var app = builder.Build();

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

    var projectRepo = scope.ServiceProvider.GetRequiredService<IRepository<Project>>();
    var localId = ProjectId.From("local");
    if (!await projectRepo.Query.AnyAsync(p => p.Id == localId, CancellationToken.None))
    {
        await projectRepo.AddAsync(Project.Create(localId, "Local", null), CancellationToken.None);
        await projectRepo.SaveChangesAsync(CancellationToken.None);
    }
}

// SPEC-20260915-api-authorization-hardening RF-001/RF-002: the whole API
// requires an authenticated principal (cookie or X-Api-Key); only the
// endpoints below carry AllowAnonymous.
var api = app.MapGroup("/api").RequireAuthorization();

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
api.MapGet("local/ai/catalog", (AiCatalogService catalog) => Results.Ok(new { models = catalog.List() }));
api.MapPost("local/ai/catalog", (AiChatModelDto model, AiCatalogService catalog) =>
{
    var added = catalog.TryAdd(model);
    return added ? Results.Created($"/api/local/ai/catalog/{model.Id}", new { model }) : Results.Conflict(new { error = new { code = "MODEL_EXISTS", message = $"Model '{model.Id}' already exists." } });
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

api.MapGet("local/ai/threads/{id}/events", async (HttpResponse response, string id, IRepository<AiChatEvent> eventRepo, IThreadEventStreamService threadEvents, CancellationToken ct) =>
{
    var threadId = AiChatThreadId.From(id);
    var existing = await eventRepo.Query.Where(e => e.ThreadId == threadId).OrderBy(e => e.CreatedAt).Select(e => e.ToDto()).ToListAsync(ct);

    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    foreach (var ev in existing)
    {
        await response.WriteAsync($"event: ai_chat.event\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(ev, ApiJsonOptions.Default)}\n\n", ct);
    }

    await response.Body.FlushAsync(ct);

    await foreach (var ev in threadEvents.SubscribeAsync(id, ct))
    {
        await response.WriteAsync($"event: {ev.Type}\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(ev.Payload, ApiJsonOptions.Default)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
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

api.MapGet("device-workspaces", async (IRepository<WorkflowWorkspace> workspaceRepo, CancellationToken ct) =>
{
    var workspaces = await workspaceRepo.ListAsync(ct);
    return Results.Ok(new { workspaces = workspaces.Select(w => w.ToDto()) });
});
api.MapPut("device-workspaces", async (
    UpdateDeviceWorkspaceRequest request,
    IRepository<Project> projectRepo,
    IRepository<WorkflowWorkspace> workspaceRepo,
    CancellationToken ct) =>
{
    var projectId = ProjectId.From(request.ProjectId);
    var project = await projectRepo.GetAsync(projectId, ct);
    if (project is null)
    {
        return Results.NotFound(new { error = new { code = "PROJECT_NOT_FOUND", message = $"Project '{request.ProjectId}' not found." } });
    }

    var workspaceJson = request.Workspace.GetRawText();
    var existing = await workspaceRepo.GetAsync(projectId, ct);
    if (existing is null)
    {
        var workspace = WorkflowWorkspace.Create(projectId, workspaceJson);
        await workspaceRepo.AddAsync(workspace, ct);
    }
    else
    {
        existing.Update(workspaceJson);
        await workspaceRepo.UpdateAsync(existing, ct);
    }

    await workspaceRepo.SaveChangesAsync(ct);

    var updated = await workspaceRepo.GetAsync(projectId, ct);
    return Results.Ok(new { workspace = updated?.ToDto() });
});

api.MapGet("workflow-capabilities", (WorkflowCapabilityService capabilities) => Results.Ok(new { capabilities = capabilities.List() }));

api.MapPut("workflow-capabilities", (UpdateWorkflowCapabilitiesRequest request, WorkflowCapabilityService capabilities) =>
{
    var capability = capabilities.Upsert(request);
    return Results.Ok(new { capability });
});

var projects = api.MapGroup("projects");

projects.MapGet("", async (IRepository<Project> projectRepo, IRepository<DomainTask> taskRepo, CancellationToken ct) =>
{
    var projectsList = await projectRepo.ListAsync(ct);
    var allTasks = await taskRepo.ListAsync(ct);

    var dtos = projectsList
        .Select(p => p.ToDto(allTasks.Count(t => t.ProjectId == p.Id && !t.ArchivedAt.HasValue)))
        .ToList();

    return Results.Ok(new { projects = dtos });
});

projects.MapPost("", async (CreateProjectRequest request, IRepository<Project> projectRepo, IEventStreamService events, CancellationToken ct) =>
{
    var id = string.IsNullOrWhiteSpace(request.Id) ? ProjectId.NewGuid() : ProjectId.From(request.Id);

    var existing = await projectRepo.Query.AnyAsync(p => p.Id == id, ct);
    if (existing)
    {
        return Results.Conflict(new { error = new { code = "PROJECT_EXISTS", message = $"Project '{id.Value}' already exists." } });
    }

    var project = Project.Create(id, request.Name, request.WorkspacePath);
    await projectRepo.AddAsync(project, ct);
    await projectRepo.SaveChangesAsync(ct);

    return Results.Created($"/api/projects/{project.Id.Value}", new { project = project.ToDto(0L) });
});

projects.MapGet("{id}", async (string id, IRepository<Project> projectRepo, IRepository<DomainTask> taskRepo, CancellationToken ct) =>
{
    var projectId = ProjectId.From(id);
    var project = await projectRepo.GetAsync(projectId, ct);
    if (project is null)
    {
        return Results.NotFound(new { error = new { code = "PROJECT_NOT_FOUND", message = $"Project '{id}' not found." } });
    }

    var issueCount = await taskRepo.Query.CountAsync(t => t.ProjectId == projectId && !t.ArchivedAt.HasValue, ct);
    return Results.Ok(new { project = project.ToDto(issueCount) });
});

var tasks = api.MapGroup("tasks");

tasks.MapGet("", async (
    HttpRequest request,
    IRepository<DomainTask> taskRepo,
    IRepository<Project> projectRepo,
    CancellationToken ct) =>
{
    var projectId = request.Query["projectId"].FirstOrDefault();
    var status = request.Query["status"].FirstOrDefault();
    var archivedValue = request.Query["archived"].FirstOrDefault();
    var q = request.Query["q"].FirstOrDefault();
    var assigneeId = request.Query["assigneeId"].FirstOrDefault();
    var label = request.Query["label"].FirstOrDefault();

    var archived = bool.TryParse(archivedValue, out var archivedParsed) ? archivedParsed : (bool?)null;

    var allTasks = await taskRepo.ListAsync(ct);
    IEnumerable<DomainTask> filtered = allTasks;

    if (!string.IsNullOrWhiteSpace(projectId))
    {
        filtered = filtered.Where(t => t.ProjectId.Value == projectId);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        filtered = filtered.Where(t => t.Status.Value == status);
    }

    if (archived == false)
    {
        filtered = filtered.Where(t => !t.ArchivedAt.HasValue);
    }
    else if (archived == true)
    {
        filtered = filtered.Where(t => t.ArchivedAt.HasValue);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
        var lower = q.ToLowerInvariant();
        filtered = filtered.Where(t =>
            t.Title.ToLowerInvariant().Contains(lower)
            || (t.Description?.ToLowerInvariant().Contains(lower) ?? false)
            || t.Identifier.Value.ToLowerInvariant().Contains(lower));
    }

    if (!string.IsNullOrWhiteSpace(assigneeId))
    {
        filtered = filtered.Where(t => t.Assignee?.Id == assigneeId);
    }

    if (!string.IsNullOrWhiteSpace(label))
    {
        filtered = filtered.Where(t => t.Labels.Contains(label, StringComparer.Ordinal));
    }

    var result = filtered
        .OrderBy(t => t.SortOrder ?? double.MaxValue)
        .ThenBy(t => t.CreatedAt)
        .Select(t => t.ToDto())
        .ToList();

    ProjectDto? projectDto = null;
    if (!string.IsNullOrWhiteSpace(projectId))
    {
        var project = await projectRepo.GetAsync(new ProjectId(projectId), ct);
        projectDto = project?.ToDto(result.Count);
    }

    return Results.Ok(new TaskListDto(result, projectDto));
});

tasks.MapPost("", async (
    CreateTaskRequest request,
    IRepository<Project> projectRepo,
    IRepository<DomainTask> taskRepo,
    IEventStreamService events,
    CancellationToken ct) =>
{
    var project = await projectRepo.GetAsync(new ProjectId(request.ProjectId), ct);
    if (project is null)
    {
        return Results.NotFound(new { error = new { code = "PROJECT_NOT_FOUND", message = $"Project '{request.ProjectId}' not found." } });
    }

    var status = TaskStatus.From(request.Status ?? "todo");
    var priority = TaskPriority.From(request.Priority ?? "medium");
    var identifier = project.GenerateTaskIdentifier();
    var task = DomainTask.Create(
        TaskId.NewGuid(),
        identifier,
        project.Id,
        request.Title,
        request.Description,
        status,
        priority,
        request.Creator ?? Actor.LocalUser(),
        request.Labels,
        null,
        DateTime.UtcNow);

    if (request.SortOrder.HasValue || request.StartDate.HasValue || request.DueDate.HasValue)
    {
        task.ApplyPatch(new TaskPatch(SortOrder: request.SortOrder, StartDate: request.StartDate, DueDate: request.DueDate), task.Version);
    }

    await taskRepo.AddAsync(task, ct);
    await projectRepo.UpdateAsync(project, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task, project);

    return Results.Created($"/api/tasks/{task.Id.Value}", new { task = task.ToDto() });
});

tasks.MapGet("{id}", async (string id, IRepository<DomainTask> taskRepo, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    return Results.Ok(new { task = task.ToDto() });
});

tasks.MapPatch("{id}", async (string id, UpdateTaskRequest request, IRepository<DomainTask> taskRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    task.ApplyPatch(request.Changes, request.Version);
    await taskRepo.UpdateAsync(task, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task);

    return Results.Ok(new { task = task.ToDto() });
});

tasks.MapDelete("{id}", async (string id, long version, IRepository<DomainTask> taskRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    if (task.Version != version)
    {
        return Results.Conflict(new { error = new { code = TaskboardDomainErrorCodes.VersionConflict, message = $"Expected version {version} but found {task.Version}." } });
    }

    task.Delete(Actor.LocalUser());
    await taskRepo.DeleteAsync(task, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task);

    return Results.NoContent();
});

tasks.MapPost("{id}/move", async (string id, [FromBody] MoveTaskRequest request, IRepository<DomainTask> taskRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    task.Move(TaskStatus.From(request.Status), request.SortOrder, Actor.LocalUser());
    await taskRepo.UpdateAsync(task, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task);

    return Results.Ok(new { task = task.ToDto() });
});

tasks.MapPost("{id}/archive", async (string id, IRepository<DomainTask> taskRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    task.Archive(Actor.LocalUser());
    await taskRepo.UpdateAsync(task, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task);

    return Results.Ok(new { task = task.ToDto() });
});

tasks.MapPost("{id}/restore", async (string id, IRepository<DomainTask> taskRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    task.Restore(Actor.LocalUser());
    await taskRepo.UpdateAsync(task, ct);
    await taskRepo.SaveChangesAsync(ct);

    await PublishEventsAsync(events, task);

    return Results.Ok(new { task = task.ToDto() });
});

tasks.MapGet("{id}/comments", async (string id, IRepository<Comment> commentRepo, CancellationToken ct) =>
{
    var comments = await commentRepo.Query
        .Where(c => c.TaskId == new TaskId(id))
        .OrderBy(c => c.CreatedAt)
        .ToListAsync(ct);

    return Results.Ok(new { comments = comments.Select(c => c.ToDto()) });
});

tasks.MapPost("{id}/comments", async (string id, CreateCommentRequest request, IRepository<DomainTask> taskRepo, IRepository<Comment> commentRepo, IEventStreamService events, CancellationToken ct) =>
{
    var task = await taskRepo.GetAsync(new TaskId(id), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    var comment = Comment.Create(CommentId.NewGuid(), task.Id, request.Body, Actor.LocalUser());
    await commentRepo.AddAsync(comment, ct);
    await commentRepo.SaveChangesAsync(ct);

    await events.PublishAsync(new ServerSentEvent("comment.added", new CommentAddedDomainEvent(task.Id, comment.Id)), ct);

    return Results.Created($"/api/tasks/{id}/comments/{comment.Id.Value}", new { comment = comment.ToDto() });
});

tasks.MapPatch("{taskId}/comments/{commentId}", async (string taskId, string commentId, UpdateCommentRequest request, IRepository<Comment> commentRepo, CancellationToken ct) =>
{
    var comment = await commentRepo.GetAsync(new CommentId(commentId), ct);
    if (comment is null || comment.TaskId.Value != taskId)
    {
        return Results.NotFound(new { error = new { code = "COMMENT_NOT_FOUND", message = $"Comment '{commentId}' not found." } });
    }

    comment.Edit(request.Body);
    await commentRepo.UpdateAsync(comment, ct);
    await commentRepo.SaveChangesAsync(ct);

    return Results.Ok(new { comment = comment.ToDto() });
});

tasks.MapDelete("{taskId}/comments/{commentId}", async (string taskId, string commentId, IRepository<Comment> commentRepo, CancellationToken ct) =>
{
    var comment = await commentRepo.GetAsync(new CommentId(commentId), ct);
    if (comment is null || comment.TaskId.Value != taskId)
    {
        return Results.NotFound(new { error = new { code = "COMMENT_NOT_FOUND", message = $"Comment '{commentId}' not found." } });
    }

    await commentRepo.DeleteAsync(comment, ct);
    await commentRepo.SaveChangesAsync(ct);

    return Results.NoContent();
});

tasks.MapGet("{id}/attachments", async (string id, IRepository<Attachment> attachmentRepo, CancellationToken ct) =>
{
    var taskAttachments = await attachmentRepo.Query
        .Where(a => a.TaskId == new TaskId(id))
        .OrderBy(a => a.CreatedAt)
        .ToListAsync(ct);

    return Results.Ok(new { attachments = taskAttachments.Select(a => a.ToDto()) });
});

tasks.MapGet("{id}/activities", async (string id, IRepository<TaskActivity> activityRepo, CancellationToken ct) =>
{
    var activities = await activityRepo.Query
        .Where(a => a.TaskId == new TaskId(id))
        .OrderBy(a => a.Timestamp)
        .ToListAsync(ct);

    return Results.Ok(new { activities = activities.Select(a => new { a.Id, a.TaskId, a.Actor, a.Changes, a.Timestamp }) });
});

tasks.MapGet("{id}/relations", async (string id, IRepository<TaskRelation> relationRepo, CancellationToken ct) =>
{
    var relations = await relationRepo.Query
        .Where(r => r.SourceTaskId == new TaskId(id) || r.TargetTaskId == new TaskId(id))
        .ToListAsync(ct);

    return Results.Ok(new { relations = relations.Select(r => new { r.Id, SourceTaskId = r.SourceTaskId.Value, TargetTaskId = r.TargetTaskId.Value, Type = r.RelationType.Value, r.CreatedAt }) });
});

tasks.MapPost("{id}/relations", async (string id, [FromBody] CreateRelationRequest request, IRepository<DomainTask> taskRepo, IRepository<TaskRelation> relationRepo, CancellationToken ct) =>
{
    var sourceTask = await taskRepo.GetAsync(new TaskId(id), ct);
    if (sourceTask is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{id}' not found." } });
    }

    var relation = TaskRelation.Create(
        new TaskId(id),
        new TaskId(request.TargetTaskId),
        RelationType.From(request.Type));

    await relationRepo.AddAsync(relation, ct);
    await relationRepo.SaveChangesAsync(ct);

    return Results.Created($"/api/tasks/{id}/relations/{request.Type}/{request.TargetTaskId}", new { relation = new { relation.Id, SourceTaskId = relation.SourceTaskId.Value, TargetTaskId = relation.TargetTaskId.Value, Type = relation.RelationType.Value, relation.CreatedAt } });
});

tasks.MapDelete("{id}/relations/{type}/{targetTaskId}", async (string id, string type, string targetTaskId, IRepository<TaskRelation> relationRepo, CancellationToken ct) =>
{
    var relation = await relationRepo.Query.FirstOrDefaultAsync(
        r => r.SourceTaskId == new TaskId(id)
             && r.TargetTaskId == new TaskId(targetTaskId)
             && r.RelationType == RelationType.From(type),
        ct);

    if (relation is null)
    {
        return Results.NotFound(new { error = new { code = "RELATION_NOT_FOUND", message = "Relation not found." } });
    }

    await relationRepo.DeleteAsync(relation, ct);
    await relationRepo.SaveChangesAsync(ct);

    return Results.NoContent();
});

var attachments = api.MapGroup("attachments");

attachments.MapPost("", async (
    HttpRequest request,
    IRepository<DomainTask> taskRepo,
    IRepository<Attachment> attachmentRepo,
    IEventStreamService events,
    CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    var taskId = form["taskId"].FirstOrDefault();
    var commentIdValue = form["commentId"].FirstOrDefault();
    var kindValue = form["kind"].FirstOrDefault() ?? "attachment";

    if (file is null || string.IsNullOrWhiteSpace(taskId))
    {
        return Results.BadRequest(new { error = new { code = "INVALID_ATTACHMENT", message = "Missing file or taskId." } });
    }

    var task = await taskRepo.GetAsync(new TaskId(taskId), ct);
    if (task is null)
    {
        return Results.NotFound(new { error = new { code = "TASK_NOT_FOUND", message = $"Task '{taskId}' not found." } });
    }

    var attachmentId = AttachmentId.NewGuid();
    var attachmentDir = Path.Combine(dataDir, "attachments", attachmentId.Value);
    Directory.CreateDirectory(attachmentDir);
    var filePath = Path.Combine(attachmentDir, file.FileName);

    await using (var stream = File.Create(filePath))
    {
        await file.CopyToAsync(stream, ct);
    }

    CommentId? commentId = null;
    if (!string.IsNullOrWhiteSpace(commentIdValue))
    {
        commentId = new CommentId(commentIdValue);
    }

    var attachment = Attachment.Create(
        attachmentId,
        task.Id,
        new AttachmentKind(kindValue),
        file.FileName,
        file.ContentType,
        file.Length,
        filePath,
        DateTime.UtcNow,
        commentId);

    await attachmentRepo.AddAsync(attachment, ct);
    await attachmentRepo.SaveChangesAsync(ct);

    await events.PublishAsync(new ServerSentEvent("attachment.created", attachment.ToDto()), ct);

    return Results.Created($"/api/attachments/{attachment.Id.Value}", new { attachment = attachment.ToDto() });
}).DisableAntiforgery();

attachments.MapGet("{id}/content", async (string id, IRepository<Attachment> attachmentRepo, CancellationToken ct) =>
{
    var attachment = await attachmentRepo.GetAsync(new AttachmentId(id), ct);
    if (attachment is null || !File.Exists(attachment.Path))
    {
        return Results.NotFound(new { error = new { code = "ATTACHMENT_NOT_FOUND", message = $"Attachment '{id}' not found." } });
    }

    return Results.File(attachment.Path, attachment.ContentType, enableRangeProcessing: true);
});

attachments.MapGet("{id}/download", async (string id, IRepository<Attachment> attachmentRepo, CancellationToken ct) =>
{
    var attachment = await attachmentRepo.GetAsync(new AttachmentId(id), ct);
    if (attachment is null || !File.Exists(attachment.Path))
    {
        return Results.NotFound(new { error = new { code = "ATTACHMENT_NOT_FOUND", message = $"Attachment '{id}' not found." } });
    }

    return Results.File(attachment.Path, attachment.ContentType, attachment.Filename, enableRangeProcessing: true);
});

attachments.MapDelete("{id}", async (string id, IRepository<Attachment> attachmentRepo, IEventStreamService events, CancellationToken ct) =>
{
    var attachment = await attachmentRepo.GetAsync(new AttachmentId(id), ct);
    if (attachment is null)
    {
        return Results.NotFound(new { error = new { code = "ATTACHMENT_NOT_FOUND", message = $"Attachment '{id}' not found." } });
    }

    await attachmentRepo.DeleteAsync(attachment, ct);
    await attachmentRepo.SaveChangesAsync(ct);

    if (File.Exists(attachment.Path))
    {
        File.Delete(attachment.Path);
    }

    await events.PublishAsync(new ServerSentEvent("attachment.deleted", new { attachment.Id.Value }), ct);

    return Results.NoContent();
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
    CancellationToken ct) =>
{
    var issue = await gitHub.UpdateIssueColumnAsync(
        $"{owner}/{repo}", number, request.OldColumn, request.NewColumn, ct);
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
    await orchestration.EnqueueAsync(request, ct);
    return Results.Accepted();
});

agents.MapGet("logs/{issueId}", async (string issueId, IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    var logs = await orchestration.GetLogsAsync(issueId, ct);
    return Results.Ok(new { logs });
});

agents.MapPost("executions/{issueId}/cancel", async (string issueId, IAgentOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.CancelAsync(issueId, ct);
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

app.MapSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Taskboard AI v1");
    options.DocumentTitle = "Taskboard AI - API";
    options.RoutePrefix = "swagger";
});

app.MapFallbackToFile("index.html");

app.Run();

static ServerSentEvent MapDomainEvent(IDomainEvent domainEvent)
{
    return domainEvent switch
    {
        TaskCreatedDomainEvent e => new ServerSentEvent("task.created", e),
        TaskMovedDomainEvent e => new ServerSentEvent("task.moved", e),
        TaskUpdatedDomainEvent e => new ServerSentEvent("task.updated", e),
        TaskArchivedDomainEvent e => new ServerSentEvent("task.archived", e),
        TaskRestoredDomainEvent e => new ServerSentEvent("task.restored", e),
        TaskDeletedDomainEvent e => new ServerSentEvent("task.deleted", e),
        CommentAddedDomainEvent e => new ServerSentEvent("comment.added", e),
        ProjectLabelsUpdatedDomainEvent e => new ServerSentEvent("project.labels_updated", e),
        _ => new ServerSentEvent("domain.event", domainEvent)
    };
}

static async System.Threading.Tasks.Task PublishEventsAsync(IEventStreamService eventStream, params object[] aggregates)
{
    var events = new List<IDomainEvent>();

    foreach (var aggregate in aggregates)
    {
        if (aggregate is Project project)
        {
            events.AddRange(project.DomainEvents);
            project.ClearDomainEvents();
        }
        else if (aggregate is DomainTask task)
        {
            events.AddRange(task.DomainEvents);
            task.ClearDomainEvents();
        }
    }

    foreach (var domainEvent in events)
    {
        await eventStream.PublishAsync(MapDomainEvent(domainEvent));
    }
}

