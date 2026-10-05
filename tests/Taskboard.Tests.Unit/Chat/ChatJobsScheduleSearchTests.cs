using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.Chat;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Chat;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Harness.Security;
using Taskboard.Repositories;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-jobs-schedule-search: parser cron puro, serviço de
/// schedules em SQLite (cap, entrega única em falta, rearm de cron),
/// ChatJobService com processo real (echo/sleep, kill, sweep de órfãos),
/// índice FTS5 e tools job_*/schedule_*/session_search com flags.
/// </summary>
public sealed class ChatJobsScheduleSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dataDir;
    private readonly TaskboardDbContext _context;
    private readonly ChatProvider _provider;
    private readonly List<HttpClient> _httpClients = [];
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<ChatJobService> _jobServices = [];

    public ChatJobsScheduleSearchTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-chat-jss-{Guid.NewGuid()}.sqlite");
        _dataDir = Path.Join(Path.GetTempPath(), $"tb-jss-{Guid.NewGuid()}");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _provider = ChatProvider.Create("fake", "http://provider.test", "sk-test");
        _context.ChatProviders.Add(_provider);
        _context.SaveChanges();
    }

    private TaskboardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        return new TaskboardDbContext(options);
    }

    private ChatService NewChatService(
        TaskboardDbContext context,
        IChatMessageSearchIndex? searchIndex = null,
        HttpMessageHandler? handler = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Tools:Enabled"] = "true",
                ["Taskboard:Chat:MaxToolIterations"] = "4",
            })
            .Build();
        var tools = new Dictionary<string, IChatTool>(StringComparer.Ordinal)
        {
            ["echo_tool"] = new FakeEchoTool(),
        };
        var client = new HttpClient(handler ?? new PlainTextProviderHandler());
        _httpClients.Add(client);
        return new ChatService(
            new EfCoreRepository<ChatProvider>(context),
            new EfCoreRepository<ChatConversation>(context),
            new EfCoreRepository<ChatMessage>(context),
            new OpenAiCompatibleClient(client),
            new ChatCapabilityRegistry(tools, new FakeSkillDiscovery(), configuration),
            new FakeSkillDiscovery(),
            new FakeWorkspaceResolver(),
            configuration,
            new ChatRunCoordinator(),
            NewHybridCache(),
            new EfCoreRepository<ChatRun>(context),
            new ChatRunQueue(),
            new ChatRunBroadcaster(),
            new EfCoreRepository<ChatApproval>(context),
            new EfCoreRepository<ChatSteer>(context),
            new ChatApprovalCoordinator(),
            [],
            NullLogger<ChatService>.Instance,
            searchIndex: searchIndex);
    }

    private ChatScheduleService NewScheduleService(
        TaskboardDbContext context, ChatService chat,
        Dictionary<string, string?>? extraConfig = null,
        TimeProvider? clock = null)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Taskboard:Chat:Schedule:Enabled"] = "true",
        };
        if (extraConfig is not null)
        {
            foreach (var kv in extraConfig)
            {
                configValues[kv.Key] = kv.Value;
            }
        }

        return new ChatScheduleService(
            new EfCoreRepository<ChatSchedule>(context),
            new EfCoreRepository<ChatConversation>(context),
            chat,
            new ConfigurationBuilder().AddInMemoryCollection(configValues).Build(),
            NullLogger<ChatScheduleService>.Instance,
            clock);
    }

    /// <summary>Scope factory that resolves per-scope EF contexts over the
    /// same SQLite file + a fresh ChatService — mirrors the server DI.</summary>
    private IServiceScopeFactory NewScopeFactory(ChatService? chat = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IRepository<ChatJob>>(sp =>
            new EfCoreRepository<ChatJob>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped<IRepository<ChatSchedule>>(sp =>
            new EfCoreRepository<ChatSchedule>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(sp =>
            chat ?? NewChatService(sp.GetRequiredService<TaskboardDbContext>()));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private ChatJobService NewJobService(IServiceScopeFactory scopeFactory)
    {
        var service = new ChatJobService(
            scopeFactory, new PassThroughRedactor(),
            NullLogger<ChatJobService>.Instance, _dataDir);
        _jobServices.Add(service);
        return service;
    }

    /// <summary>Scope factory whose IRepository&lt;ChatJob&gt; delays every
    /// SaveChanges — widens the StartAsync insert window so a settle racing
    /// ahead of the row insert is deterministic, not load-dependent.</summary>
    private IServiceScopeFactory NewScopeFactoryComPersistenciaLenta()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IRepository<ChatJob>>(sp =>
            new DelayedSaveChatJobRepository(
                new EfCoreRepository<ChatJob>(sp.GetRequiredService<TaskboardDbContext>()),
                TimeSpan.FromMilliseconds(400)));
        services.AddScoped(sp => NewChatService(sp.GetRequiredService<TaskboardDbContext>()));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class DelayedSaveChatJobRepository(
        IRepository<ChatJob> inner, TimeSpan delay) : IRepository<ChatJob>
    {
        public IQueryable<ChatJob> Query => inner.Query;

        public Task<ChatJob?> GetAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => inner.GetAsync(id, cancellationToken);

        public Task<IReadOnlyList<ChatJob>> ListAsync(CancellationToken cancellationToken = default)
            => inner.ListAsync(cancellationToken);

        public Task AddAsync(ChatJob entity, CancellationToken cancellationToken = default)
            => inner.AddAsync(entity, cancellationToken);

        public Task UpdateAsync(ChatJob entity, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(ChatJob entity, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(entity, cancellationToken);

        public void Untrack(ChatJob entity) => inner.Untrack(entity);

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            await inner.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ChatConversation> NewConversationAsync()
    {
        var conversation = ChatConversation.Create(
            ChatConversationId.NewGuid(), _provider.Id, "fake-model", "Test");
        _context.ChatConversations.Add(conversation);
        await _context.SaveChangesAsync();
        return conversation;
    }

    private static HybridCache NewHybridCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static async Task<ChatJobDto> WaitForStatusAsync(
        IChatJobService jobs, string conversationId, string jobId,
        string wanted, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var list = await jobs.ListAsync(conversationId, activeOnly: false);
            var job = list.FirstOrDefault(j => j.Id == jobId);
            if (job?.Status == wanted)
            {
                return job;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"job {jobId} nunca atingiu '{wanted}'");
    }

    // ---------- RF-004: parser cron ----------

    [Fact]
    public void Dado_CronDiario_Quando_NextAfter_Entao_ProximoDiaNoHorario()
    {
        var cron = ChatCronSchedule.Parse("0 9 * * *");

        var next = cron.NextAfter(new DateTime(2026, 10, 5, 14, 0, 0, DateTimeKind.Utc));

        next.ShouldBe(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_CronQuinzeMinutos_Quando_NextAfter_Entao_QuartoDeHora()
    {
        var cron = ChatCronSchedule.Parse("*/15 * * * *");

        var next = cron.NextAfter(new DateTime(2026, 10, 5, 13, 7, 30, DateTimeKind.Utc));

        next.ShouldBe(new DateTime(2026, 10, 5, 13, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_CronDiasUteis_Quando_SextaAposHorario_Entao_Segunda()
    {
        var cron = ChatCronSchedule.Parse("0 9 * * 1-5");

        // 2026-10-09 é sexta; após as 9h o próximo disparo é segunda 12/10 às 9h.
        var next = cron.NextAfter(new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc));

        next.ShouldBe(new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc));
        next.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public void Dado_CronDomEDow_Quando_Ambos_Entao_Uniao()
    {
        // RF-004: com dom e dow restritos aplica a união (cron clássico).
        var cron = ChatCronSchedule.Parse("0 0 20 * 1");

        var next = cron.NextAfter(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc));

        // 2026-10-05 é segunda (dow=1) → dispara sem ser dia 20.
        next.ShouldBe(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_CronInterrogacao_Quando_Parse_Entao_EquivalenteAsterisco()
    {
        var cron = ChatCronSchedule.Parse("0 9 ? * 1");

        var next = cron.NextAfter(new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc));

        next.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    [InlineData("* * * *")] // 4 campos
    public void Dado_CronInvalida_Quando_Parse_Entao_LancaFormato(string expression)
    {
        Should.Throw<FormatException>(() => ChatCronSchedule.Parse(expression));
    }

    // ---------- RF-004/RF-005: ChatScheduleService ----------

    [Fact]
    public async Task Dado_AfterSeconds_Quando_Cria_Entao_ProximoDisparoFuturo()
    {
        var conversation = await NewConversationAsync();
        var service = NewScheduleService(_context, NewChatService(_context));
        var before = DateTime.UtcNow;

        var schedule = await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("after_seconds", AfterSeconds: 120, Prompt: "ping"));

        schedule.Active.ShouldBeTrue();
        schedule.NextFireAtUtc.ShouldBeGreaterThan(before.AddSeconds(100));
        schedule.Title.ShouldBe("ping");
    }

    [Fact]
    public async Task Dado_CapAtingido_Quando_Cria_Entao_Conflito()
    {
        var conversation = await NewConversationAsync();
        var service = NewScheduleService(_context, NewChatService(_context),
            new Dictionary<string, string?> { ["Taskboard:Chat:Schedule:MaxPerConversation"] = "1" });
        await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("after_seconds", AfterSeconds: 60, Prompt: "p1"));

        await Should.ThrowAsync<ChatConflictException>(() =>
            service.CreateAsync(conversation.Id.Value,
                new CreateChatScheduleRequest("after_seconds", AfterSeconds: 60, Prompt: "p2")));
    }

    [Fact]
    public async Task Dado_AtNoPassado_Quando_Cria_Entao_Validacao()
    {
        var conversation = await NewConversationAsync();
        var service = NewScheduleService(_context, NewChatService(_context));

        await Should.ThrowAsync<ChatValidationException>(() =>
            service.CreateAsync(conversation.Id.Value,
                new CreateChatScheduleRequest("at", Expr: "2020-01-01T00:00:00Z", Prompt: "p")));
    }

    [Fact]
    public async Task Dado_CronInvalida_Quando_Cria_Entao_Validacao()
    {
        var conversation = await NewConversationAsync();
        var service = NewScheduleService(_context, NewChatService(_context));

        await Should.ThrowAsync<ChatValidationException>(() =>
            service.CreateAsync(conversation.Id.Value,
                new CreateChatScheduleRequest("cron", Expr: "99 99 99 99 99", Prompt: "p")));
    }

    [Fact]
    public async Task Dado_AgendamentoDevido_Quando_Entrega_Entao_EnfileiraEDesativa()
    {
        var conversation = await NewConversationAsync();
        var chat = NewChatService(_context);
        var service = NewScheduleService(_context, chat);
        var schedule = await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("after_seconds", AfterSeconds: 1, Prompt: "tarefa agendada"));
        await Task.Delay(1100); // fica devido

        var delivered = await service.DeliverDueAsync(DateTime.UtcNow);

        delivered.ShouldBe(1);
        var row = await _context.ChatSchedules.SingleAsync(s => s.Id == ChatScheduleId.From(schedule.Id));
        row.Active.ShouldBeFalse("one-shot desativa após a entrega");
        row.LastDeliveredAt.ShouldNotBeNull();
        // RF-006: a entrega vira uma run normal com mensagem user Kind=schedule.
        var message = await _context.ChatMessages
            .Where(m => m.ConversationId == conversation.Id && m.Content == "tarefa agendada")
            .SingleAsync();
        message.Kind.ShouldBe(ChatMessageKinds.Schedule);
        (await _context.ChatRuns.AnyAsync(r => r.ConversationId == conversation.Id))
            .ShouldBeTrue("a entrega enfileira um ChatRun");
    }

    [Fact]
    public async Task Dado_CronVencido_Quando_Entrega_Entao_ReagendaNoFuturo()
    {
        var conversation = await NewConversationAsync();
        var chat = NewChatService(_context);
        var service = NewScheduleService(_context, chat);
        var schedule = await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("cron", Expr: "* * * * *", Prompt: "tick"));

        // Força o "missed fire": retrocede NextFireAtUtc para o passado.
        var row = await _context.ChatSchedules.SingleAsync(s => s.Id == ChatScheduleId.From(schedule.Id));
        row.MarkDelivered(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-5));
        row.SetActive(true, DateTime.UtcNow.AddMinutes(-5));
        await _context.SaveChangesAsync();
        var now = DateTime.UtcNow;

        var delivered = await service.DeliverDueAsync(now);

        delivered.ShouldBe(1); // RF-006: disparos perdidos colapsam em UM envio
        var reloaded = await _context.ChatSchedules.SingleAsync(s => s.Id == row.Id);
        reloaded.Active.ShouldBeTrue("cron permanece ativo");
        reloaded.NextFireAtUtc.ShouldBeGreaterThan(now);
        reloaded.NextFireAtUtc.ShouldBeLessThan(now.AddMinutes(2));
    }

    [Fact]
    public async Task Dado_AgendamentoDeOutraConversa_Quando_Atualiza_Entao_NaoEncontra()
    {
        var conversation = await NewConversationAsync();
        var other = await NewConversationAsync();
        var service = NewScheduleService(_context, NewChatService(_context));
        var schedule = await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("after_seconds", AfterSeconds: 60, Prompt: "p"));

        var updated = await service.UpdateAsync(other.Id.Value, schedule.Id,
            new PatchChatScheduleRequest(Active: false));

        updated.ShouldBeNull();
        (await service.DeleteAsync(other.Id.Value, schedule.Id)).ShouldBeFalse();
        (await _context.ChatSchedules.AnyAsync(s => s.Id == ChatScheduleId.From(schedule.Id)))
            .ShouldBeTrue("row de outra conversa não pode ser deletada");
    }

    [Fact]
    public async Task Dado_AgendamentoPausado_Quando_DeliverDue_Entao_NaoEntrega()
    {
        var conversation = await NewConversationAsync();
        var chat = NewChatService(_context);
        var service = NewScheduleService(_context, chat);
        var schedule = await service.CreateAsync(conversation.Id.Value,
            new CreateChatScheduleRequest("after_seconds", AfterSeconds: 1, Prompt: "pausado"));
        await service.UpdateAsync(conversation.Id.Value, schedule.Id,
            new PatchChatScheduleRequest(Active: false));
        await Task.Delay(1100);

        var delivered = await service.DeliverDueAsync(DateTime.UtcNow);

        delivered.ShouldBe(0);
    }

    // ---------- RF-001/RF-002 + boot-sweep: ChatJobService ----------

    [Fact]
    public async Task Dado_ComandoEcho_Quando_BackgroundJob_Entao_FinalizaComSaida()
    {
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());

        var job = await jobs.StartAsync(conversation.Id.Value, null, "echo hello-job", _dataDir);

        var finished = await WaitForStatusAsync(
            jobs, conversation.Id.Value, job.Id, "finished", TimeSpan.FromSeconds(15));
        finished.ExitCode.ShouldBe(0);
        var output = await jobs.GetOutputAsync(conversation.Id.Value, job.Id, tailBytes: 4096);
        output.ShouldNotBeNull();
        output.OutputTail.ShouldContain("hello-job");
        // RF-001: a conclusão vira nota de sistema no transcript.
        (await _context.ChatMessages.AnyAsync(m =>
            m.ConversationId == conversation.Id && m.Content.Contains("hello-job")))
            .ShouldBeTrue("a conclusão do job posta uma system note");
    }

    [Fact]
    public async Task Dado_JobInstantaneo_Quando_PersistenciaLenta_Entao_SettleNaoSePerde()
    {
        // Race real vista no CI de main: o runner de um processo instantâneo
        // podia fazer settle antes do AddAsync+SaveChanges do StartAsync —
        // SettleAsync não achava a linha, retornava cedo e o job ficava
        // 'running' para sempre. O gate do StartAsync só libera o runner
        // depois da linha persistida.
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactoryComPersistenciaLenta());

        var job = await jobs.StartAsync(conversation.Id.Value, null, "echo race-gate", _dataDir);

        var finished = await WaitForStatusAsync(
            jobs, conversation.Id.Value, job.Id, "finished", TimeSpan.FromSeconds(15));
        finished.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_ComandoLongo_Quando_Kill_Entao_StatusKilled()
    {
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());
        var job = await jobs.StartAsync(conversation.Id.Value, null, "sleep 30", _dataDir);

        var killed = await jobs.KillAsync(conversation.Id.Value, job.Id);

        killed.ShouldNotBeNull();
        var terminal = await WaitForStatusAsync(
            jobs, conversation.Id.Value, job.Id, "killed", TimeSpan.FromSeconds(15));
        terminal.Status.ShouldBe("killed");
        // Segunda tentativa → conflito 409 (RF-002).
        await Should.ThrowAsync<ChatJobConflictException>(() =>
            jobs.KillAsync(conversation.Id.Value, job.Id));
    }

    [Fact]
    public async Task Dado_JobOrfaoDeBoot_Quando_Sweep_Entao_TerminatedENota()
    {
        var conversation = await NewConversationAsync();
        // Simula o restart: row 'running' gravada mas sem handle vivo.
        var orphan = ChatJob.Create(
            ChatJobId.NewGuid(), conversation.Id, null, "sleep 999");
        orphan.MarkRunning(999_999, Path.Join(_dataDir, "ghost.log"));
        _context.ChatJobs.Add(orphan);
        await _context.SaveChangesAsync();

        var jobs = NewJobService(NewScopeFactory());
        await jobs.TerminateOrphansAsync();

        // Re-read num contexto novo — _context segue a instância tracked
        // seedada (running) na identity-map.
        await using var check = NewContext();
        var row = await check.ChatJobs.SingleAsync(j => j.Id == orphan.Id);
        row.Status.ShouldBe(ChatJobStatus.Terminated);
        (await check.ChatMessages.AnyAsync(m =>
            m.ConversationId == conversation.Id && m.Content.Contains("terminated")))
            .ShouldBeTrue("o sweep deixa uma nota de sistema por linha órfã");
    }

    [Fact]
    public async Task Dado_JobDeOutraConversa_Quando_Opera_Entao_NaoEncontra()
    {
        var conversation = await NewConversationAsync();
        var other = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());
        var job = await jobs.StartAsync(conversation.Id.Value, null, "sleep 5", _dataDir);

        (await jobs.GetOutputAsync(other.Id.Value, job.Id, 100)).ShouldBeNull();
        (await jobs.KillAsync(other.Id.Value, job.Id)).ShouldBeNull();
    }

    // ---------- RF-007/RF-008: FTS5 ----------

    [Fact]
    public async Task Dado_MensagemPersistida_Quando_Busca_Entao_EncontraComMark()
    {
        var index = new ChatMessageSearchIndex(_context);
        var chat = NewChatService(_context, searchIndex: index);
        var conversation = await NewConversationAsync();

        await chat.EnqueueMessageAsync(conversation.Id.Value, "lembrar do codigo-secreto-42");

        var hits = await index.SearchAsync("codigo-secreto-42", null, 10);
        hits.Count.ShouldBe(1);
        hits[0].ConversationId.ShouldBe(conversation.Id.Value);
        hits[0].Snippet.ShouldContain("<mark>");
    }

    [Fact]
    public async Task Dado_DuasConversas_Quando_BuscaComFiltro_Entao_SomenteConversa()
    {
        var index = new ChatMessageSearchIndex(_context);
        var chat = NewChatService(_context, searchIndex: index);
        var a = await NewConversationAsync();
        var b = await NewConversationAsync();
        await chat.EnqueueMessageAsync(a.Id.Value, "termo-unico-alpha");
        await chat.EnqueueMessageAsync(b.Id.Value, "termo-unico-alpha");

        var hits = await index.SearchAsync("termo-unico-alpha", a.Id.Value, 10);

        hits.Count.ShouldBe(1);
        hits[0].ConversationId.ShouldBe(a.Id.Value);
    }

    [Fact]
    public async Task Dado_TextoComOperadoresFts_Quando_Busca_Entao_NaoQuebra()
    {
        var index = new ChatMessageSearchIndex(_context);
        var chat = NewChatService(_context, searchIndex: index);
        var conversation = await NewConversationAsync();
        await chat.EnqueueMessageAsync(conversation.Id.Value, "conteúdo normal");

        // " OR / aspas / parênteses são quotados — não viram sintaxe MATCH.
        var hits = await index.SearchAsync("\" OR ) conteúdo", null, 10);

        hits.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_ConversaRemovida_Quando_Busca_Entao_SemResultados()
    {
        var index = new ChatMessageSearchIndex(_context);
        var chat = NewChatService(_context, searchIndex: index);
        var conversation = await NewConversationAsync();
        await chat.EnqueueMessageAsync(conversation.Id.Value, "termo-vai-sair");

        await index.RemoveConversationAsync(conversation.Id.Value);

        (await index.SearchAsync("termo-vai-sair", null, 10)).ShouldBeEmpty();
    }

    // ---------- RF-002/RF-005/RF-010: tools ----------

    private static ChatToolContext ToolContext(string? conversationId = null) => new(
        WorkspacePath: "",
        ProviderId: Guid.NewGuid(),
        ProviderBaseUrl: "http://p.test",
        ProviderApiKey: "sk-x",
        ImageModel: "",
        SearchBackend: "none",
        SearchUrl: "",
        SearchApiKey: "",
        ConversationId: conversationId);

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    [Fact]
    public async Task Dado_JobAtivo_Quando_JobList_Entao_RetornaJob()
    {
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());
        var job = await jobs.StartAsync(conversation.Id.Value, null, "sleep 5", _dataDir);
        var tool = new JobListTool(jobs, Config());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement,
            ToolContext(conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain(job.Id);
    }

    [Fact]
    public async Task Dado_FlagJobsDesligada_Quando_JobList_Entao_Recusado()
    {
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());
        var tool = new JobListTool(jobs,
            Config(("Taskboard:Chat:Jobs:Enabled", "false")));

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement,
            ToolContext(conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldNotBeNull().ShouldContain("disabled");
    }

    [Fact]
    public async Task Dado_ScheduleCreateValido_Quando_Tool_Entao_CriaAgendamento()
    {
        var conversation = await NewConversationAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped(sp => NewScheduleService(
            sp.GetRequiredService<TaskboardDbContext>(),
            NewChatService(sp.GetRequiredService<TaskboardDbContext>())));
        services.AddScoped<IChatScheduleService>(sp => sp.GetRequiredService<ChatScheduleService>());
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var tool = new ScheduleCreateTool(
            provider.GetRequiredService<IServiceScopeFactory>(), Config());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"kind":"after_seconds","after_seconds":600,"prompt":"lembrete"}""").RootElement,
            ToolContext(conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        (await _context.ChatSchedules.AnyAsync(s => s.ConversationId == conversation.Id))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_FlagScheduleDesligada_Quando_ScheduleCreate_Entao_Recusado()
    {
        var tool = new ScheduleCreateTool(TestScopeFactory.Empty(),
            Config(("Taskboard:Chat:Schedule:Enabled", "false")));

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"kind":"after_seconds","after_seconds":60,"prompt":"x"}""").RootElement,
            ToolContext("conv"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldNotBeNull().ShouldContain("disabled");
    }

    [Fact]
    public async Task Dado_IndicePopulado_Quando_SessionSearch_Entao_RetornaHit()
    {
        var conversation = await NewConversationAsync();
        var index = new ChatMessageSearchIndex(_context);
        var chat = NewChatService(_context, searchIndex: index);
        await chat.EnqueueMessageAsync(conversation.Id.Value, "palavra-alvo-session");
        var services = new ServiceCollection();
        services.AddSingleton<IChatMessageSearchIndex>(index);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var tool = new SessionSearchTool(
            provider.GetRequiredService<IServiceScopeFactory>(), Config());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"palavra-alvo-session"}""").RootElement,
            ToolContext(conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("palavra-alvo-session");
    }

    [Fact]
    public async Task Dado_FlagSearchDesligada_Quando_SessionSearch_Entao_Recusado()
    {
        var tool = new SessionSearchTool(TestScopeFactory.Empty(),
            Config(("Taskboard:Chat:Search:Enabled", "false")));

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"q"}""").RootElement,
            ToolContext("conv"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.RefusalReason.ShouldNotBeNull().ShouldContain("disabled");
    }

    [Fact]
    public async Task Dado_RunInBackground_Quando_ShellExec_Entao_RetornaJobId()
    {
        var conversation = await NewConversationAsync();
        var jobs = NewJobService(NewScopeFactory());
        var tool = new ShellExecTool(
            new DynamicCommandClassifier(),
            new SecretScrubber(),
            jobs: jobs, configuration: Config());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"command":"echo bg-out","run_in_background":true}""").RootElement,
            ToolContext(conversation.Id.Value), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"jobId\"");
        result.Json.ShouldContain("running");
        var list = await jobs.ListAsync(conversation.Id.Value, activeOnly: false);
        list.ShouldContain(j => j.Command == "echo bg-out");
    }

    [Fact]
    public async Task Dado_FlagJobsDesligada_Quando_BackgroundExec_Entao_Recusado()
    {
        var tool = new ShellExecTool(
            new DynamicCommandClassifier(),
            new SecretScrubber(),
            jobs: NewJobService(NewScopeFactory()),
            configuration: Config(("Taskboard:Chat:Jobs:Enabled", "false")));

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"command":"echo x","run_in_background":true}""").RootElement,
            ToolContext("conv"), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---------- helpers ----------

    private sealed class PassThroughRedactor : ISecretRedactor
    {
        public string? Redact(string? text) => text;
    }

    private sealed class PlainTextProviderHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""",
                    Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FakeEchoTool : IChatTool
    {
        public string Name => "echo_tool";
        public string Description => "echo";
        public string ParametersJson => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        public Task<ChatToolResult> ExecuteAsync(JsonElement arguments, ChatToolContext context, CancellationToken ct) =>
            Task.FromResult(new ChatToolResult("{\"ok\":true}"));
    }

    private sealed class FakeSkillDiscovery : Taskboard.Application.Contracts.Skills.ISkillDiscoveryService
    {
        public Task<IReadOnlyList<Taskboard.Application.Contracts.Skills.SkillDto>> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Taskboard.Application.Contracts.Skills.SkillDto>>([]);

        public Task<Taskboard.Application.Contracts.Skills.SkillDetailDto?> GetDetailAsync(
            string source, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<Taskboard.Application.Contracts.Skills.SkillDetailDto?>(null);

        public Task<Taskboard.Application.Contracts.Skills.SkillFileResult> GetFileAsync(
            string source, string name, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Taskboard.Application.Contracts.Skills.SkillFileResult(
                Taskboard.Application.Contracts.Skills.SkillFileError.NotFound, null, null));
    }

    private sealed class FakeWorkspaceResolver : IWorkspacePathResolver
    {
        public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
        {
            exists = false;
            return Path.GetTempPath();
        }

        public string? NormalizeWorkspacePath(string? path) => path;

        public WorkspaceDirsDto? ListSubdirs(string? path) => null;
    }

    public void Dispose()
    {
        foreach (var jobs in _jobServices)
        {
            jobs.Dispose();
        }

        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        foreach (var client in _httpClients)
        {
            client.Dispose();
        }

        _context.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            if (Directory.Exists(_dataDir))
            {
                Directory.Delete(_dataDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
