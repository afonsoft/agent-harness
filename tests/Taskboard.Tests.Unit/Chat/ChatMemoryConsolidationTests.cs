using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Server.Services;
using Taskboard.Tests.Unit.Jobs;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-F01/F02: consolidação da memória do
/// chat — dedupe de fatos idênticos, expiração por MaxAgeDays (default 90),
/// cap LRU em MaxFacts (default 500) e o relatório {kept,deduped,expired,
/// evicted} que vira linha do job no cockpit.
/// </summary>
public sealed class ChatMemoryConsolidationTests : IDisposable
{
    private readonly string _dir;
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    public ChatMemoryConsolidationTests()
    {
        _dir = Path.Join(Path.GetTempPath(), $"mem-cons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ---- store-level Consolidate ----

    [Fact]
    public void Dado_FatosIdenticos_Quando_Consolida_Entao_DedupeMantemMaisRecente()
    {
        var store = new ChatMemoryStore(_dir);
        var older = store.Add("gosta de café", Now.AddDays(-10));
        var newer = store.Add("gosta de café", Now.AddDays(-1));
        store.Add("outro fato", Now.AddDays(-2));

        var report = store.Consolidate(Now.AddDays(-90), maxFacts: 500);

        report.Deduped.ShouldBe(1);
        report.Expired.ShouldBe(0);
        report.Evicted.ShouldBe(0);
        report.Kept.ShouldBe(2);
        var items = store.List();
        items.Count.ShouldBe(2);
        items.ShouldContain(m => m.Id == newer.Id);
        items.ShouldNotContain(m => m.Id == older.Id);
    }

    [Fact]
    public void Dado_FatosExpirados_Quando_Consolida_Entao_RemovePorIdade()
    {
        var store = new ChatMemoryStore(_dir);
        store.Add("fato velho", Now.AddDays(-100));
        store.Add("fato novo", Now.AddDays(-5));

        var report = store.Consolidate(Now.AddDays(-90), maxFacts: 500);

        report.Expired.ShouldBe(1);
        report.Kept.ShouldBe(1);
        store.List().ShouldHaveSingleItem().Content.ShouldBe("fato novo");
    }

    [Fact]
    public void Dado_ExpiradoEDuplicado_Quando_Consolida_Entao_ExpiraAntesDeDedupir()
    {
        var store = new ChatMemoryStore(_dir);
        store.Add("mesmo fato", Now.AddDays(-100));
        store.Add("mesmo fato", Now.AddDays(-1));

        var report = store.Consolidate(Now.AddDays(-90), maxFacts: 500);

        report.Expired.ShouldBe(1);
        report.Deduped.ShouldBe(0, "a cópia expirada sai pela idade antes do dedupe contar");
        report.Kept.ShouldBe(1);
    }

    [Fact]
    public void Dado_AcimaDoCap_Quando_Consolida_Entao_LruDescartaMaisAntigos()
    {
        var store = new ChatMemoryStore(_dir);
        for (var i = 0; i < 10; i++)
        {
            store.Add($"fato {i}", Now.AddDays(-i));
        }

        var report = store.Consolidate(Now.AddDays(-90), maxFacts: 5);

        report.Evicted.ShouldBe(5);
        report.Kept.ShouldBe(5);
        var items = store.List();
        items.Select(m => m.Content).ShouldBe(["fato 0", "fato 1", "fato 2", "fato 3", "fato 4"]);
    }

    [Fact]
    public void Dado_StoreVazia_Quando_Consolida_Entao_RelatorioZerado()
    {
        var store = new ChatMemoryStore(_dir);

        var report = store.Consolidate(Now.AddDays(-90), maxFacts: 500);

        report.ShouldBe(new ChatMemoryConsolidationReport(0, 0, 0, 0));
        File.Exists(Path.Join(_dir, "chat-memory.json")).ShouldBeFalse("nada para escrever");
    }

    // ---- job-level ----

    private static MemoryConsolidationService NewJob(ChatMemoryStore store, IConfiguration configuration) =>
        new(store, configuration, JobRegistryTestHost.Create(),
            NullLogger<MemoryConsolidationService>.Instance);

    [Fact]
    public async Task Dado_SeedVariado_Quando_JobRoda_Entao_RelatorioNoResultado()
    {
        var store = new ChatMemoryStore(_dir);
        store.Add("dup", Now.AddDays(-30));
        store.Add("dup", Now.AddDays(-2));
        store.Add("antigo", Now.AddDays(-120));
        store.Add("recente", Now.AddDays(-1));
        var job = NewJob(store, Config(new()));

        var message = await job.ConsolidateOnce(CancellationToken.None) ?? throw new InvalidOperationException("report expected");

        message.ShouldNotBeNull();
        message.ShouldContain("kept 2");
        message.ShouldContain("deduped 1");
        message.ShouldContain("expired 1");
        message.ShouldContain("evicted 0");
        store.List().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_ConfigCustom_Quando_JobRoda_Entao_RespeitaJanelaECap()
    {
        var store = new ChatMemoryStore(_dir);
        store.Add("margem", Now.AddDays(-31));
        store.Add("ok", Now.AddDays(-1));
        store.Add("a", Now.AddDays(-1));
        store.Add("b", Now.AddDays(-2));
        var job = NewJob(store, Config(new()
        {
            [MemoryConsolidationService.MaxAgeDaysKey] = "30",
            [MemoryConsolidationService.MaxFactsKey] = "2",
        }));

        var message = await job.ConsolidateOnce(CancellationToken.None) ?? throw new InvalidOperationException("report expected");

        message.ShouldContain("expired 1");
        message.ShouldContain("evicted 1");
        store.List().Select(m => m.Content).ShouldBe(["ok", "a"]);
    }

    [Fact]
    public async Task Dado_MaxAgeZero_Quando_JobRoda_Entao_NuncaExpira()
    {
        var store = new ChatMemoryStore(_dir);
        store.Add("muito antigo", Now.AddDays(-4000));
        var job = NewJob(store, Config(new()
        {
            [MemoryConsolidationService.MaxAgeDaysKey] = "0",
        }));

        var message = await job.ConsolidateOnce(CancellationToken.None) ?? throw new InvalidOperationException("report expected");

        message.ShouldContain("expired 0");
        store.List().ShouldHaveSingleItem();
    }
}
