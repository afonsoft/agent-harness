using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Application.Mapping;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Taskboard.Requests;
using Taskboard.ValueObjects;

namespace Taskboard.Application.AiChat;

public sealed class AiChatService
{
    private const string ModelTierDefault = "default";

    private readonly IRepository<AiChatThread> _threadRepo;
    private readonly IRepository<AiChatRun> _runRepo;
    private readonly IRepository<AiChatEvent> _eventRepo;
    private readonly ILlmProvider _llmProvider;
    private readonly IThreadEventStreamService _threadEvents;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAgentEligibilityService _eligibility;
    private readonly ICliChatRunner _cliChatRunner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiChatService> _logger;
    private readonly IWorkspacePathResolver _workspace;
    private readonly IAgentModelConfigService _modelConfig;
    private readonly IAgentModelCatalogService _modelCatalog;
    private readonly IAgentCliDefinitionRepository _cliDefinitions;
    private readonly IAgentDiscoveryService _discovery;
    private readonly IContainerCliDiscovery _containerDiscovery;

    public AiChatService(
        IRepository<AiChatThread> threadRepo,
        IRepository<AiChatRun> runRepo,
        IRepository<AiChatEvent> eventRepo,
        ILlmProvider llmProvider,
        IThreadEventStreamService threadEvents,
        IServiceScopeFactory serviceScopeFactory,
        IAgentEligibilityService eligibility,
        ICliChatRunner cliChatRunner,
        IConfiguration configuration,
        ILogger<AiChatService> logger,
        IWorkspacePathResolver workspace,
        IAgentModelConfigService modelConfig,
        IAgentModelCatalogService modelCatalog,
        IAgentCliDefinitionRepository cliDefinitions,
        IAgentDiscoveryService discovery,
        IContainerCliDiscovery containerDiscovery)
    {
        _threadRepo = threadRepo;
        _runRepo = runRepo;
        _eventRepo = eventRepo;
        _llmProvider = llmProvider;
        _threadEvents = threadEvents;
        _serviceScopeFactory = serviceScopeFactory;
        _eligibility = eligibility;
        _cliChatRunner = cliChatRunner;
        _configuration = configuration;
        _logger = logger;
        _workspace = workspace;
        _modelConfig = modelConfig;
        _modelCatalog = modelCatalog;
        _cliDefinitions = cliDefinitions;
        _discovery = discovery;
        _containerDiscovery = containerDiscovery;
    }

    /// <summary>
    /// SPEC-20260929-docker-cli-context RF-002: with <c>ContainerContext</c>,
    /// CLI availability is validated against the binaries discovered inside
    /// that container — never against the host PATH. A container not seen by
    /// discovery (stopped, unknown name) fails closed.
    /// </summary>
    private static string? ResolveOpenHandsBinary(AgentType agentType) =>
        agentType == AgentType.OpenHands ? "openhands" : null;

    private async Task EnsureContainerCliAsync(string containerContext, AgentType agentType, CancellationToken ct)
    {
        var containers = await _containerDiscovery.ListContainersAsync(ct).ConfigureAwait(false);
        var container = containers.FirstOrDefault(
            c => string.Equals(c.Name, containerContext.Trim(), StringComparison.Ordinal));
        if (container is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Container '{containerContext}' is not running or not allowed.");
        }

        var binary = AgentCliMap.CliKindFor(agentType) is { } kind
            ? AgentCliMap.GetSpec(kind)?.Binary
            : ResolveOpenHandsBinary(agentType);
        if (binary is null || !container.AvailableClis.Contains(binary, StringComparer.Ordinal))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"CLI for agent '{agentType}' is not installed in container '{containerContext}'.");
        }
    }

    public async Task<AiChatThreadDto> CreateThreadAsync(
        CreateAiChatThreadRequest request,
        Actor actor,
        CancellationToken ct = default)
    {
        // SPEC-20260928-ai-code-generic-cli RF-002: a custom CLI definition
        // (AgentCliId) is a valid backing CLI — its def IS the eligibility
        // record (enabled + executable declared); no AgentType gate applies.
        var (agentType, agentCliId, transport) = await ResolveCliBindingAsync(request, ct);
        if (transport is not ("acp" or "pty"))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Invalid transport '{request.Transport}' — expected 'acp' or 'pty'.");
        }

        var (modelName, tier, modelSource) = await ResolveModelChoiceAsync(request, agentType, ct);

        var model = ModelRef.From(modelName);
        var reasoningEffort = string.IsNullOrWhiteSpace(request.ReasoningEffort) ? "medium" : request.ReasoningEffort;
        var sandbox = string.IsNullOrWhiteSpace(request.Sandbox) ? Sandbox.WorkspaceWrite : Sandbox.From(request.Sandbox);

        var thread = CreateThreadEntity(request, agentType, model, reasoningEffort, sandbox);

        // SPEC-20260928-ai-code-generic-cli: transport/container/custom-CLI
        // binding — immutable after creation (ConfigureCli is creation-time).
        thread.ConfigureCli(transport, request.ContainerContext, agentCliId);
        thread.SetModelChoice(tier?.ToString(), modelSource);

        await _threadRepo.AddAsync(thread, ct);
        await _threadRepo.SaveChangesAsync(ct);

        return thread.ToDto();
    }

    private async Task<(AgentType? AgentType, string? AgentCliId, string Transport)> ResolveCliBindingAsync(
        CreateAiChatThreadRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.AgentCliId))
        {
            var (cliId, customTransport) = await ResolveCustomCliAsync(request, ct);
            return (null, cliId, customTransport);
        }

        // SPEC-20260921-ai-chat-cli-backend RF-002: every thread is backed
        // by an eligible agent CLI — there is no direct-LLM provider.
        if (string.IsNullOrWhiteSpace(request.AgentType) ||
            !Enum.TryParse<AgentType>(request.AgentType, true, out var parsedType))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Invalid agent type '{request.AgentType}'.");
        }

        var transport = string.IsNullOrWhiteSpace(request.Transport)
            ? "acp"
            : request.Transport.Trim().ToLowerInvariant();
        await EnsureBuiltinCliAsync(request, parsedType, transport, ct);
        return (parsedType, null, transport);
    }

    private async Task<(string AgentCliId, string Transport)> ResolveCustomCliAsync(
        CreateAiChatThreadRequest request, CancellationToken ct)
    {
        // AgentCliId is validated non-null upstream; the ! is required (nullable enabled).
        var def = await _cliDefinitions.GetAsync(request.AgentCliId!.Trim(), ct);
        if (def is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Custom CLI '{request.AgentCliId}' does not exist.");
        }

        if (!def.Enabled)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Custom CLI '{def.DisplayName}' is disabled.");
        }

        // The declared transport is the def's; an explicit request value
        // wins so ACP-capable defs can still open a terminal view (Q3).
        var transport = string.IsNullOrWhiteSpace(request.Transport)
            ? def.Transport
            : request.Transport.Trim().ToLowerInvariant();

        // SPEC-20260929-ai-chat-capabilities RF-002: structured chat needs
        // an AgentType-bound ACP adapter — custom defs spawn via argv only
        // (PTY). Refuse the dead path at creation instead of a thread that
        // can never execute a prompt.
        if (transport is "acp")
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Custom CLI '{def.DisplayName}' cannot serve the chat view — ACP sessions require a builtin agent. Use the terminal view.");
        }

        return (def.Id, transport);
    }

    private async Task EnsureBuiltinCliAsync(
        CreateAiChatThreadRequest request, AgentType parsedType, string transport, CancellationToken ct)
    {
        // Container context shifts availability checks to the binaries
        // discovered inside the container (SPEC-20260929-docker-cli-context
        // RF-002) — a host install is neither required nor sufficient.
        var inContainer = !string.IsNullOrWhiteSpace(request.ContainerContext)
            && !string.Equals(request.ContainerContext, "host", StringComparison.OrdinalIgnoreCase);
        if (transport is "acp")
        {
            // SPEC-20260929-ai-chat-capabilities RF-001: chat requires a
            // CLI that actually speaks ACP — eligibility alone must not
            // admit PTY-only CLIs into structured threads.
            if (!AgentCliMap.SupportsAcp(parsedType))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Agent '{parsedType}' has no structured chat (ACP) support — use the terminal view.");
            }

            if (inContainer)
            {
                await EnsureContainerCliAsync(request.ContainerContext!, parsedType, ct);
                return;
            }

            var eligible = await _eligibility.GetEligibleTypesAsync(ct);
            if (!eligible.Contains(parsedType))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.AgentNotEligible,
                    $"Agent '{parsedType}' is not eligible — the CLI must be installed, authenticated and enabled.");
            }

            return;
        }

        if (inContainer)
        {
            await EnsureContainerCliAsync(request.ContainerContext!, parsedType, ct);
        }
        else if (_discovery.ResolveExecutablePath(parsedType) is null)
        {
            // PTY threads only need the binary on PATH — authentication
            // happens inside the terminal itself.
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"CLI for agent '{parsedType}' is not installed — cannot open a terminal thread.");
        }
    }

    // SPEC-20260921-ai-code-thread-config RF-004: modelo explícito vence;
    // vazio + tier → resolução via config service (override tem precedência
    // sobre curated); ambos vazios → CLI default. RF-006: a origem do
    // modelo efetivo é auditada em ModelSource.
    private async Task<(string ModelName, AgentModelTier? Tier, string? ModelSource)> ResolveModelChoiceAsync(
        CreateAiChatThreadRequest request, AgentType? agentType, CancellationToken ct)
    {
        AgentModelTier? tier = null;
        if (!string.IsNullOrWhiteSpace(request.ModelTier))
        {
            if (!Enum.TryParse<AgentModelTier>(request.ModelTier, true, out var parsed))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Invalid model tier '{request.ModelTier}' — expected Lite, Normal or Ultra.");
            }

            tier = parsed;
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            var modelName = request.Model.Trim();
            var modelSource = agentType is { } modelType
                ? await ResolveModelSourceAsync(modelType, modelName, ct)
                : "custom";
            return (modelName, tier, modelSource);
        }

        if (tier is not null && agentType is { } configType)
        {
            var config = await _modelConfig.GetConfigAsync(configType, ct);
            var resolved = tier switch
            {
                AgentModelTier.Lite => config.Lite,
                AgentModelTier.Ultra => config.Ultra,
                _ => config.Normal,
            };
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return (ModelTierDefault, tier, null);
            }

            var source = string.Equals(config.Source, "override", StringComparison.OrdinalIgnoreCase)
                ? "custom"
                : "curated";
            return (resolved, tier, source);
        }

        // Tier without a builtin agent (custom CLI) → CLI default.
        return (ModelTierDefault, tier, null);
    }

    private AiChatThread CreateThreadEntity(
        CreateAiChatThreadRequest request, AgentType? agentType,
        ModelRef model, string reasoningEffort, Sandbox sandbox)
    {
        if (!string.Equals(request.Mode, "agent", StringComparison.OrdinalIgnoreCase))
        {
            return AiChatThread.Create(
                AiChatThreadId.NewGuid(),
                request.Title,
                model,
                reasoningEffort,
                sandbox,
                agentType: agentType,
                repositoryFullName: request.RepositoryFullName);
        }

        // SPEC-20260921-ai-code-thread-config RF-003: o repositório resolve
        // ~/repos/<name> quando WorkspacePath não é informado; o path manual
        // sempre vence; falha de resolução é 400, nunca fallback silencioso.
        var workspacePath = request.WorkspacePath;
        if (string.IsNullOrWhiteSpace(workspacePath) && !string.IsNullOrWhiteSpace(request.RepositoryFullName))
        {
            var resolved = _workspace.ResolveCardWorkdir(request.RepositoryFullName, out var exists);
            if (!exists)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Repository '{request.RepositoryFullName}' has no local workspace — clone it under the workspace root or provide an explicit WorkspacePath.");
            }

            workspacePath = resolved;
        }

        return AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(),
            request.Title,
            model,
            reasoningEffort,
            sandbox,
            agentType,
            workspacePath,
            request.RepositoryFullName);
    }

    /// <summary>
    /// Tags which catalog served an explicitly chosen model — probe (reported
    /// by the CLI), the curated table, or a custom catalog entry. Unknown
    /// names are tagged "custom": the user typed it, so it is by definition
    /// not served by a known catalog.
    /// </summary>
    private async Task<string> ResolveModelSourceAsync(AgentType agentType, string modelName, CancellationToken ct)
    {
        try
        {
            var reported = await _modelCatalog.ListAvailableAsync(agentType, cancellationToken: ct);
            if (reported.Contains(modelName, StringComparer.Ordinal))
            {
                return "probe";
            }
        }
        catch (Exception ex)
        {
            // Probe failure degrades the audit tag, never the creation itself.
            _logger.LogWarning(ex, "Model probe failed for agent '{AgentType}' while tagging model source.", agentType);
        }

        if (AgentCliModels.Catalog(agentType).Contains(modelName, StringComparer.Ordinal))
        {
            return "curated";
        }

        return "custom";
    }

    public async Task<AiChatThreadDto?> GetThreadAsync(AiChatThreadId id, CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(id, ct);
        return thread?.ToDto();
    }

    public async Task<IReadOnlyList<AiChatThreadDto>> ListThreadsAsync(CancellationToken ct = default)
    {
        var threads = await _threadRepo.ListAsync(ct);
        return threads.Select(t => t.ToDto()).ToList().AsReadOnly();
    }

    public async Task<bool> DeleteThreadAsync(AiChatThreadId id, CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(id, ct);
        if (thread is null)
        {
            return false;
        }

        await _threadRepo.DeleteAsync(thread, ct);
        await _threadRepo.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AiChatRunDto> StartRunAsync(
        AiChatThreadId threadId,
        Actor actor,
        CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(threadId, ct);
        if (thread is null)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Thread '{threadId.Value}' not found.");
        }

        // SPEC-20260928-ai-code-generic-cli: terminal (pty) threads talk to
        // the CLI through the PTY pane — there is no structured run flow.
        if (string.Equals(thread.Transport, "pty", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Terminal threads have no agent runs — interact through the terminal pane.");
        }

        // SPEC-20260921-ai-chat-cli-backend RF-005: legacy assistant threads
        // without a bound agent auto-migrate to the first eligible CLI.
        if (thread.Mode == "assistant" && thread.AgentType is null)
        {
            var eligible = await _eligibility.GetEligibleTypesAsync(ct);
            var pick = eligible.OrderBy(t => t.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (eligible.Count > 0)
            {
                // Keep the stored model only when it is a real model of the
                // bound CLI; legacy provider names (gpt-4o, …) reset to default.
                var model = AgentCliModels.Catalog(pick).Contains(thread.Model.Value, StringComparer.Ordinal)
                    ? thread.Model
                    : ModelRef.From(ModelTierDefault);
                thread.BindAgent(pick, model);
                await _threadRepo.UpdateAsync(thread, ct);
            }
        }

        var run = thread.StartRun();
        await _runRepo.AddAsync(run, ct);
        await _threadRepo.SaveChangesAsync(ct);

        await _threadEvents.PublishAsync(
            threadId.Value,
            new ServerSentEvent("ai_chat.run", run.ToDto()),
            ct);

        // SPEC-20260918-ai-chat-threads: the run must outlive this request's DI
        // scope — the repositories above are scoped and their DbContext is
        // disposed when the HTTP request ends. A fresh scope keeps the
        // background execution alive (and CancellationToken.None keeps it from
        // dying with the request token).
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AiChatService>();
            await service.ExecuteRunAsync(thread.Id, run.Id, CancellationToken.None);
        }, CancellationToken.None);

        return run.ToDto();
    }

    private async System.Threading.Tasks.Task ExecuteRunAsync(AiChatThreadId threadId, AiChatRunId runId, CancellationToken ct)
    {
        try
        {
            var run = await _runRepo.GetAsync(runId, ct);
            var thread = await _threadRepo.GetAsync(threadId, ct);

            if (run is null || thread is null) return;

            // Get conversation history
            var events = await _eventRepo.Query
                .Where(e => e.ThreadId == threadId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);

            // SPEC-20260921-ai-chat-cli-backend: assistant runs go through the
            // bound agent CLI (one-shot, transcript as prompt). The mock
            // provider is kept only behind Taskboard:AiChat:MockProvider=true
            // for dev/tests; a thread without a bound agent fails loudly.
            var mockEnabled = bool.TryParse(_configuration["Taskboard:AiChat:MockProvider"], out var mock) && mock;
            if (!mockEnabled && thread.AgentType is null)
            {
                await EmitCliEventAsync(
                    threadId,
                    "No eligible agent CLI is bound to this thread — authenticate one in Agents or Settings and send again.",
                    AiChatEventKind.Error);
                run.Fail(-1);
                thread.SetStatus(AiChatThreadStatus.Failed);
            }
            else if (!mockEnabled && thread.AgentType is not null)
            {
                await RunCliAssistantAsync(threadId, run, thread, events, ct);
            }
            else
            {
                await RunMockProviderAsync(threadId, run, thread, events, ct);
            }

            await _runRepo.UpdateAsync(run, ct);
            await _threadRepo.UpdateAsync(thread, ct);
            await _threadRepo.SaveChangesAsync(ct);

            await _threadEvents.PublishAsync(
                threadId.Value,
                new ServerSentEvent("ai_chat.run", run.ToDto()),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            await HandleRunFailureAsync(threadId, runId, ex, ct);
        }
    }

    private async Task RunCliAssistantAsync(
        AiChatThreadId threadId, AiChatRun run, AiChatThread thread,
        List<AiChatEvent> events, CancellationToken ct)
    {
        // Eligibility is dynamic (installed + authenticated + enabled) —
        // re-check at run time so disabling an agent in Settings stops
        // existing threads from executing.
        var eligibleNow = await _eligibility.GetEligibleTypesAsync(ct);
        if (!eligibleNow.Contains(thread.AgentType.GetValueOrDefault()))
        {
            await EmitCliEventAsync(
                threadId,
                $"Agent '{thread.AgentType}' is no longer eligible — it was disabled or its CLI lost authentication after this thread was created.",
                AiChatEventKind.Error);
            run.Fail(-1);
            thread.SetStatus(AiChatThreadStatus.Failed);
            return;
        }

        var prompt = AgentThreadPromptBuilder.BuildAssistantPrompt(
            thread.Title,
            events.Select(e => e.ToDto()).ToList());
        var modelName = string.Equals(thread.Model.Value, ModelTierDefault, StringComparison.OrdinalIgnoreCase)
            ? null
            : thread.Model.Value;
        var progress = new SequentialEmitProgress(this, threadId);

        var result = await _cliChatRunner.RunAsync(
            thread.Id.Value, thread.AgentType.GetValueOrDefault(), modelName, prompt, progress, ct,
            thread.ContainerContext);

        // Drain every queued write before closing the run — the run
        // record must not complete while response lines are pending.
        await progress.Completion;

        if (result.IsSuccess)
        {
            run.Complete((int)(result.Usage?.TotalTokens ?? 0));
            thread.SetStatus(AiChatThreadStatus.Idle);
            return;
        }

        run.Fail(result.ExitCode);
        thread.SetStatus(AiChatThreadStatus.Failed);
        await EmitCliEventAsync(threadId, $"Agent CLI exited with code {result.ExitCode}.", AiChatEventKind.Error);
    }

    private async Task RunMockProviderAsync(
        AiChatThreadId threadId, AiChatRun run, AiChatThread thread,
        List<AiChatEvent> events, CancellationToken ct)
    {
        var messages = new List<LlmMessage>
        {
            new("system", $"You are an AI assistant in sandbox mode: {thread.Sandbox.Value}.")
        };

        foreach (var ev in events)
        {
            messages.Add(new LlmMessage(
                ev.Role.Value switch
                {
                    "user" => "user",
                    "assistant" => "assistant",
                    _ => "system"
                },
                ev.Content));
        }

        await foreach (var chunk in _llmProvider.StreamAsync(messages, cancellationToken: ct))
        {
            if (chunk.IsComplete)
            {
                run.Complete(chunk.Usage?.TotalTokens ?? 0);
                break;
            }

            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                var chatEvent = AiChatEvent.Create(
                    AiChatEventId.NewGuid(),
                    threadId,
                    AiChatEventRole.Assistant,
                    chunk.ContentDelta);

                thread.AddEvent(chatEvent);
                await _eventRepo.AddAsync(chatEvent, ct);

                await _threadEvents.PublishAsync(
                    threadId.Value,
                    new ServerSentEvent("ai_chat.event", chatEvent.ToDto()),
                    ct);
            }
        }

        thread.SetStatus(AiChatThreadStatus.Idle);
    }

    private async Task HandleRunFailureAsync(
        AiChatThreadId threadId, AiChatRunId runId, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "AI chat run '{RunId}' on thread '{ThreadId}' failed.", runId.Value, threadId.Value);

        var run = await _runRepo.GetAsync(runId, ct);
        var thread = await _threadRepo.GetAsync(threadId, ct);

        if (run != null)
        {
            run.Fail(-1);
            await _runRepo.UpdateAsync(run, ct);
        }

        if (thread != null)
        {
            thread.SetStatus(AiChatThreadStatus.Failed);
            await _threadRepo.UpdateAsync(thread, ct);
        }

        await _threadRepo.SaveChangesAsync(ct);

        if (run != null)
        {
            await _threadEvents.PublishAsync(
                threadId.Value,
                new ServerSentEvent("ai_chat.run", run.ToDto()),
                CancellationToken.None);
        }
    }

    // IProgress<T> implementation whose Report() runs synchronously on the
    // producer (process-output) thread and appends each emit to a sequential
    // task chain — unlike Progress<T>, which dispatches callbacks to the thread
    // pool and can interleave or reorder the persisted lines.
    private sealed class SequentialEmitProgress(AiChatService service, AiChatThreadId threadId)
        : IProgress<AgentLogMessage>
    {
        private readonly object _gate = new();
        private Task _chain = Task.CompletedTask;

        public Task Completion
        {
            get { lock (_gate) { return _chain; } }
        }

        public void Report(AgentLogMessage log)
        {
            var kind = log.Stream == AgentLogStream.StdErr ? AiChatEventKind.Error : AiChatEventKind.Message;
            lock (_gate)
            {
                _chain = _chain.ContinueWith(
                    _ => service.EmitCliEventAsync(threadId, log.Content, kind),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }
        }
    }

    // Persists a CLI-produced line as a thread event in a fresh scope — the
    // progress callback fires on the process-output thread, so the scoped
    // DbContext of this run must not be touched there (same pattern as
    // AgentSessionManager.ExecuteOneShotFallbackAsync).
    private async Task EmitCliEventAsync(AiChatThreadId threadId, string content, AiChatEventKind kind)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
            var evt = AiChatEvent.CreateTyped(
                AiChatEventId.NewGuid(),
                threadId,
                AiChatEventRole.Assistant,
                content,
                kind);
            await eventRepo.AddAsync(evt);
            await eventRepo.SaveChangesAsync();
            await _threadEvents.PublishAsync(
                threadId.Value,
                new ServerSentEvent("ai_chat.event", evt.ToDto()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist CLI chat event for thread '{ThreadId}'.", threadId.Value);
        }
    }

    public async Task<AiChatEventDto> AddEventAsync(
        AiChatThreadId threadId,
        AddAiChatEventRequest request,
        Actor actor,
        CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(threadId, ct);
        if (thread is null)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Thread '{threadId.Value}' not found.");
        }

        var role = AiChatEventRole.From(request.Role);
        var kind = !string.IsNullOrWhiteSpace(request.Kind)
            ? AiChatEventKind.From(request.Kind)
            : AiChatEventKind.Message;

        var chatEvent = AiChatEvent.CreateTyped(
            AiChatEventId.NewGuid(),
            threadId,
            role,
            request.Content,
            kind,
            request.PayloadJson);

        thread.AddEvent(chatEvent);

        // B-13: threads created via "New conversation" keep the placeholder
        // title forever — derive it from the first real user prompt.
        if (role == AiChatEventRole.User && AiChatThreadTitle.IsGeneric(thread.Title))
        {
            thread.UpdateTitle(AiChatThreadTitle.Derive(request.Content));
        }

        await _eventRepo.AddAsync(chatEvent, ct);
        await _threadRepo.SaveChangesAsync(ct);

        await _threadEvents.PublishAsync(
            threadId.Value,
            new ServerSentEvent("ai_chat.event", chatEvent.ToDto()),
            ct);

        return chatEvent.ToDto();
    }

    /// <summary>
    /// SPEC-20260921-ai-code-chat-ux RF-004: creates a new thread that replays
    /// the source conversation up to (and including) the selected event, so
    /// the user can branch a session without losing the original.
    /// </summary>
    public async Task<AiChatThreadDto> ForkThreadAsync(
        AiChatThreadId id,
        string eventId,
        Actor actor,
        CancellationToken ct = default)
    {
        var source = await _threadRepo.GetAsync(id, ct)
            ?? throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Thread '{id.Value}' not found.");

        var events = await _eventRepo.Query
            .Where(e => e.ThreadId == id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var cut = events.FindIndex(e => e.Id.Value == eventId);
        if (cut < 0)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Event '{eventId}' not found in thread '{id.Value}'.");
        }

        var title = source.Title.Length > 224
            ? source.Title[..224]
            : source.Title;
        title = $"{title} (source: fork)";

        // B-09: Mode "agent" alone selects the agent branch — threads bound to
        // a custom CLI (AgentCliId, AgentType null) must keep agent mode and
        // WorkspacePath instead of silently forking as assistant.
        var fork = source.Mode == "agent"
            ? AiChatThread.CreateAgentThread(
                AiChatThreadId.NewGuid(),
                title,
                source.Model,
                source.ReasoningEffort,
                source.Sandbox,
                source.AgentType,
                source.WorkspacePath,
                source.RepositoryFullName)
            : AiChatThread.Create(
                AiChatThreadId.NewGuid(),
                title,
                source.Model,
                source.ReasoningEffort,
                source.Sandbox,
                agentType: source.AgentType,
                repositoryFullName: source.RepositoryFullName);
        fork.SetModelChoice(source.ModelTier, source.ModelSource);
        // SPEC-20260929-ai-chat-capabilities RF-003: the fork keeps the
        // source's CLI binding — transport, container context and custom def.
        fork.ConfigureCli(source.Transport, source.ContainerContext, source.AgentCliId);

        await _threadRepo.AddAsync(fork, ct);
        foreach (var copy in events.Take(cut + 1)
            .Select(ev => AiChatEvent.CreateTyped(
                AiChatEventId.NewGuid(),
                fork.Id,
                ev.Role,
                ev.Content,
                ev.Kind,
                ev.PayloadJson,
                ev.CreatedAt)))
        {
            fork.AddEvent(copy);
            await _eventRepo.AddAsync(copy, ct);
        }

        await _threadRepo.SaveChangesAsync(ct);
        return fork.ToDto();
    }

    public async Task<IReadOnlyList<AiChatEventDto>> GetEventsAsync(
        AiChatThreadId threadId,
        CancellationToken ct = default)
    {
        var events = await _eventRepo.Query
            .Where(e => e.ThreadId == threadId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return events.Select(e => e.ToDto()).ToList().AsReadOnly();
    }
}