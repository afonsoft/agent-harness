using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.CliMetrics;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.CliMetrics;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Integrations.CliDb;
using Xunit;

namespace Taskboard.Tests.Unit.CliMetrics;

public class CliMetricsServiceTests : IDisposable
{
    private readonly string _home;
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly CliDatabaseLocator _locator;

    private static readonly CliDbSource Source = new(
        "state", ".fakecli/state.db", ["sessions"], []);

    public CliMetricsServiceTests()
    {
        _home = Path.Join(Path.GetTempPath(), $"tb-clim-{Guid.NewGuid()}");
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-clim-{Guid.NewGuid()}.sqlite");
        Directory.CreateDirectory(Path.Join(_home, ".fakecli"));
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _locator = new CliDatabaseLocator(_home, Substitute.For<ILogger<CliDatabaseLocator>>());
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string WriteDbFile(string content = "db")
    {
        var path = Path.Join(_home, ".fakecli", "state.db");
        File.WriteAllText(path, content);
        return path;
    }

    private static CliSessionRecord Session(string id, DateTimeOffset started, string? model = "gpt-5") =>
        new("state", id, $"t-{id}", started, null, 3, model, 100, 50, 10);

    private CliMetricsService CriarService(params FakeExtractor[] extractors) =>
        new(new EfCoreCliMetricsRepository(_context), extractors, _locator,
            new CliMetricsOptions(), Substitute.For<ILogger<CliMetricsService>>());

    private sealed class FakeExtractor : ICliDbExtractor
    {
        public AgentCliKind Kind { get; init; } = AgentCliKind.Codex;
        public IReadOnlyList<CliDbSource> Sources { get; init; } = [Source];
        public CliDbSchemaFingerprint ExpectedFingerprint => new(0, 0, "test");
        public List<string?> CursorsSeen { get; } = [];
        public Func<string?, CliExtractionResult> OnExtract { get; set; } =
            _ => new CliExtractionResult([], [], "cursor-1", CliDbSourceStatus.Available, null);

        public Task<CliExtractionResult> ExtractSinceAsync(string? cursor, CancellationToken cancellationToken = default)
        {
            CursorsSeen.Add(cursor);
            var result = OnExtract(cursor);
            if (result is null)
            {
                throw new InvalidOperationException("null result");
            }
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Dado_FonteNova_Quando_Sync_Entao_IngereSessoesEAgrega()
    {
        WriteDbFile();
        var started = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var extractor = new FakeExtractor
        {
            OnExtract = _ => new CliExtractionResult(
                [Session("s1", started), Session("s2", started.AddHours(1))],
                [], "cursor-1", CliDbSourceStatus.Available, null),
        };

        var result = await CriarService(extractor).SyncAsync();

        result.SessionsIngested.ShouldBe(2, customMessage: "duas sessões ingeridas");
        var sessions = await _context.CliSessionMetrics.ToListAsync();
        sessions.Count.ShouldBe(2);
        sessions.ShouldAllBe(s => s.SourceId != null);

        var source = await _context.CliMetricSources.SingleAsync();
        source.Status.ShouldBe(CliDbSourceStatus.Available);
        source.WatermarkCursor.ShouldBe("cursor-1");
        source.RowCount.ShouldBe(2);

        var aggregate = await _context.CliDailyUsageAggregates.SingleAsync();
        aggregate.Day.ShouldBe("2026-09-10");
        aggregate.SessionsCount.ShouldBe(2);
        aggregate.TokensInput.ShouldBe(200);
        aggregate.ModelsUsed.ShouldBe(["gpt-5"]);
    }

    [Fact]
    public async Task Dado_ArquivoInalterado_Quando_SyncNovamente_Entao_PulaExtracao()
    {
        WriteDbFile();
        var extractor = new FakeExtractor();
        var service = CriarService(extractor);

        await service.SyncAsync();
        await service.SyncAsync();

        extractor.CursorsSeen.Count.ShouldBe(1, customMessage: "segundo sync sem mudança não extrai");
    }

    [Fact]
    public async Task Dado_ArquivoModificado_Quando_Sync_Entao_ResumeComCursor()
    {
        WriteDbFile("v1");
        var extractor = new FakeExtractor();
        var service = CriarService(extractor);

        await service.SyncAsync();
        await Task.Delay(20); // mtime granularity
        WriteDbFile("v2-changed");

        await service.SyncAsync();

        extractor.CursorsSeen.ShouldBe([null, "cursor-1"],
            customMessage: "segundo sync retoma a partir do watermark");
    }

    [Fact]
    public async Task Dado_ExternalIdRepetido_Quando_Reingere_Entao_AtualizaSemDuplicar()
    {
        WriteDbFile();
        var started = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var extractor = new FakeExtractor
        {
            OnExtract = _ => new CliExtractionResult(
                [Session("s1", started)], [], "c1", CliDbSourceStatus.Available, null),
        };
        var service = CriarService(extractor);
        await service.SyncAsync();

        await Task.Delay(20);
        WriteDbFile("v2");
        extractor.OnExtract = _ => new CliExtractionResult(
            [Session("s1", started) with { Title = "renomeado", MessageCount = 9 }],
            [], "c2", CliDbSourceStatus.Available, null);
        await service.SyncAsync();

        var sessions = await _context.CliSessionMetrics.ToListAsync();
        sessions.Count.ShouldBe(1, customMessage: "dedupe por (SourceId, ExternalId)");
        sessions[0].Title.ShouldBe("renomeado");
        sessions[0].MessageCount.ShouldBe(9);
    }

    [Fact]
    public async Task Dado_ExtratorCorrompido_Quando_Sync_Entao_MarcaErroEContinuaOutros()
    {
        WriteDbFile();
        var ruim = new FakeExtractor
        {
            Kind = AgentCliKind.OpenCode,
            OnExtract = _ => throw new CliDbReadException("corrupt db"),
        };
        var bom = new FakeExtractor { Kind = AgentCliKind.Devin };

        var result = await CriarService(ruim, bom).SyncAsync();

        result.SourcesSynced.ShouldBe(2);
        var errored = await _context.CliMetricSources
            .SingleAsync(s => s.Kind == AgentCliKind.OpenCode);
        errored.Status.ShouldBe(CliDbSourceStatus.Error);
        errored.LastError.ShouldNotBeNull().ShouldContain("corrupt db");
    }

    [Fact]
    public async Task Dado_SessaoAntiga_Quando_Sync_Entao_RetencaoPurgaRawMasMantemAgregado()
    {
        WriteDbFile();
        var antiga = DateTimeOffset.UtcNow.AddDays(-120);
        var extractor = new FakeExtractor
        {
            OnExtract = _ => new CliExtractionResult(
                [Session("old", antiga)], [], "c1", CliDbSourceStatus.Available, null),
        };
        var service = new CliMetricsService(
            new EfCoreCliMetricsRepository(_context), [extractor], _locator,
            new CliMetricsOptions { RetentionDays = 90 },
            Substitute.For<ILogger<CliMetricsService>>());

        await service.SyncAsync();

        (await _context.CliSessionMetrics.CountAsync()).ShouldBe(0,
            customMessage: "raw >90d purgado");
        (await _context.CliDailyUsageAggregates.CountAsync()).ShouldBe(1,
            customMessage: "agregado retido indefinidamente");
    }

    [Fact]
    public async Task Dado_FonteComErro_Quando_SyncSemMudanca_Entao_RetentaExtracao()
    {
        // Erro persistido com a assinatura do arquivo não pode travar a fonte —
        // caso real: cap de 512MB do build antigo deixou OpenCode preso em Error.
        WriteDbFile();
        var extractor = new FakeExtractor
        {
            OnExtract = _ => new CliExtractionResult(
                [], [], null, CliDbSourceStatus.Error, "transient failure"),
        };
        var service = CriarService(extractor);

        await service.SyncAsync();
        extractor.OnExtract = _ => new CliExtractionResult(
            [Session("s1", DateTimeOffset.UtcNow)], [], "cursor-ok",
            CliDbSourceStatus.Available, null);
        await service.SyncAsync();

        extractor.CursorsSeen.Count.ShouldBe(2,
            customMessage: "fonte em Error retenta mesmo com arquivo inalterado");
        var source = await _context.CliMetricSources.SingleAsync();
        source.Status.ShouldBe(CliDbSourceStatus.Available);
        source.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_FonteAusente_Quando_Sync_Entao_MarcaMissingSemExtrair()
    {
        // nenhum arquivo criado
        var extractor = new FakeExtractor();
        await CriarService(extractor).SyncAsync();

        extractor.CursorsSeen.ShouldBeEmpty();
        var source = await _context.CliMetricSources.SingleAsync();
        source.Status.ShouldBe(CliDbSourceStatus.Missing);
    }

    [Fact]
    public async Task Dado_SessaoDoFuturo_Quando_Ingere_Entao_ClampaTimestamp()
    {
        WriteDbFile();
        var futuro = DateTimeOffset.UtcNow.AddDays(2);
        var extractor = new FakeExtractor
        {
            OnExtract = _ => new CliExtractionResult(
                [Session("f1", futuro)], [], "c1", CliDbSourceStatus.Available, null),
        };

        await CriarService(extractor).SyncAsync();

        var session = await _context.CliSessionMetrics.SingleAsync();
        session.StartedAtUtc.ShouldBeLessThanOrEqualTo(DateTime.UtcNow,
            customMessage: "clock skew clampa para agora");
    }
}
