using System.Text.Json;
using Shouldly;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261022-session-event-log P1: append-only JSONL, generation tag,
/// adjacent migrations, refuse-future, truncated-tail flag.
/// </summary>
public class SessionEventLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "session-event-log-" + Guid.NewGuid().ToString("N"));

    private SessionEventLog NewLog() => new(_dir);

    private string FilePath(string sessionId) =>
        Path.Combine(_dir, "sessions", sessionId, "events.jsonl");

    private static JsonElement Payload(string prop, string value) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { [prop] = value });

    [Fact]
    public async Task Dado_Append_Quando_Lido_Entao_ReproduzEventosEmOrdem()
    {
        var log = NewLog();
        await log.AppendAsync("s1", SessionEventKind.RunStarted, Payload("cli", "opencode"));
        await log.AppendAsync("s1", SessionEventKind.AssistantMessage, Payload("text", "hi"));

        var result = await log.ReadAsync("s1");

        result.SessionId.ShouldBe("s1");
        result.TruncatedTail.ShouldBeFalse();
        result.Events.Count.ShouldBe(2);
        result.Events[0].Kind.ShouldBe(SessionEventKind.RunStarted);
        result.Events[0].Seq.ShouldBe(0);
        result.Events[1].Seq.ShouldBe(1);
        result.Events[0].Generation.ShouldBe(SessionEventLog.CurrentGeneration);
        result.Events[1].Payload.GetProperty("text").GetString().ShouldBe("hi");
    }

    [Fact]
    public async Task Dado_SessaoInexistente_Quando_Lida_Entao_ResultadoVazio()
    {
        var result = await NewLog().ReadAsync("nope");

        result.Events.ShouldBeEmpty();
        result.TruncatedTail.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_AppendsConcorrentes_Quando_Lidos_Entao_SeqSemGaps()
    {
        var log = NewLog();
        var tasks = Enumerable.Range(0, 20).Select(i =>
            log.AppendAsync("s1", SessionEventKind.StepBoundary, Payload("n", i.ToString())));
        await Task.WhenAll(tasks);

        var result = await log.ReadAsync("s1");
        result.Events.Select(e => e.Seq).ShouldBe(Enumerable.Range(0, 20).Select(i => (long)i));
    }

    [Fact]
    public async Task Dado_EventoGeracaoZero_Quando_Lido_Entao_MigraParaAtual()
    {
        var path = FilePath("s1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            """{"generation":0,"seq":0,"timestampUtc":"2026-10-22T00:00:00+00:00","kind":0,"payload":{"cli":"legacy"}}""" + "\n");

        var result = await NewLog().ReadAsync("s1");

        result.Events.Count.ShouldBe(1);
        result.Events[0].Generation.ShouldBe(SessionEventLog.CurrentGeneration);
        result.Events[0].Payload.GetProperty("cli").GetString().ShouldBe("legacy");
    }

    [Fact]
    public async Task Dado_EventoGeracaoFutura_Quando_Lido_Entao_Recusado()
    {
        var path = FilePath("s1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            """{"generation":99,"seq":0,"timestampUtc":"2026-10-22T00:00:00+00:00","kind":0,"payload":{}}""" + "\n");

        await Should.ThrowAsync<UnknownSessionEventGenerationException>(
            () => NewLog().ReadAsync("s1"));
    }

    [Fact]
    public async Task Dado_UltimaLinhaTruncada_Quando_Lida_Entao_SinalizadaSemDescartar()
    {
        var path = FilePath("s1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var log = NewLog();
        await log.AppendAsync("s1", SessionEventKind.RunStarted, Payload("cli", "x"));
        await File.AppendAllTextAsync(path, """{"generation":1,"seq":1,"timestampUtc":"2026-1""");

        var result = await NewLog().ReadAsync("s1");

        result.TruncatedTail.ShouldBeTrue();
        result.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_LinhaCorrompidaNoMeio_Quando_Lida_Entao_Lanca()
    {
        var path = FilePath("s1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var log = NewLog();
        await log.AppendAsync("s1", SessionEventKind.RunStarted, Payload("cli", "x"));
        await File.AppendAllTextAsync(path, "not-json\n");
        await log.AppendAsync("s1", SessionEventKind.RunCompleted, Payload("exit", "0"));

        await Should.ThrowAsync<JsonException>(() => NewLog().ReadAsync("s1"));
    }

    [Fact]
    public async Task Dado_SessionIdComTraversal_Quando_Append_Entao_Rejeitado()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            NewLog().AppendAsync("../escape", SessionEventKind.RunStarted, Payload("a", "b")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
