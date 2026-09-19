using Shouldly;
using Taskboard.Agents;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Xunit;

namespace Taskboard.Tests.Unit.CliMetrics;

public class CliMetricsEntityTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Dado_SourceRegistrada_Quando_FileStateIgual_Entao_NaoMudou()
    {
        var source = CliMetricSource.Register(
            AgentCliKind.Codex, "codex-state", ".codex/state_*.sqlite", Now);
        source.RecordFileState("/h/.codex/state_1.sqlite", 1000, 500, Now);

        source.FileChangedSinceLastSync("/h/.codex/state_1.sqlite", 1000, 500).ShouldBeFalse();
        source.FileChangedSinceLastSync("/h/.codex/state_1.sqlite", 2000, 500).ShouldBeTrue();
        source.FileChangedSinceLastSync("/h/.codex/state_1.sqlite", 1000, 600).ShouldBeTrue();
    }

    [Fact]
    public void Dado_Source_Quando_MarkSync_Entao_AtualizaCursorStatusErro()
    {
        var source = CliMetricSource.Register(AgentCliKind.Codex, "s", "p", Now);
        source.MarkSync(CliDbSourceStatus.Available, "cursor-1", rowCount: 42, now: Now);

        source.Status.ShouldBe(CliDbSourceStatus.Available);
        source.WatermarkCursor.ShouldBe("cursor-1");
        source.RowCount.ShouldBe(42);
        source.LastError.ShouldBeNull();
        source.LastSyncUtc.ShouldBe(Now);

        source.MarkError("drift detectado", Now.AddMinutes(1));
        source.Status.ShouldBe(CliDbSourceStatus.Error);
        source.LastError.ShouldBe("drift detectado");
    }

    [Fact]
    public void Dado_RegistroSemKind_Quando_Criar_Entao_Excecao()
    {
        Should.Throw<DomainException>(
            () => CliMetricSource.Register(AgentCliKind.Codex, "", "p", Now));
        Should.Throw<DomainException>(
            () => CliMetricSource.Register(AgentCliKind.Codex, "s", "", Now));
    }

    [Fact]
    public void Dado_SessionNova_Quando_Update_Entao_CamposMutaveisMudamEVersionIncrementa()
    {
        var session = CliSessionMetric.Create(
            CliMetricSourceId.NewGuid(), AgentCliKind.OpenCode, "ext-1", "titulo",
            Now, null, 3, "gpt-x", 10, 20, 5, Now);
        var v1 = session.Version;

        session.Update("titulo2", Now.AddHours(1), 9, "gpt-y", 11, 21, 6, Now.AddHours(1));

        session.Title.ShouldBe("titulo2");
        session.MessageCount.ShouldBe(9);
        session.ModelName.ShouldBe("gpt-y");
        session.TokensInput.ShouldBe(11);
        session.Version.ShouldBe(v1 + 1);
    }

    [Fact]
    public void Dado_StartedNoFuturo_Quando_Criar_Entao_ClampedParaAgora()
    {
        var futuro = Now.AddDays(30);
        var session = CliSessionMetric.Create(
            CliMetricSourceId.NewGuid(), AgentCliKind.Codex, "x", null,
            futuro, null, null, null, null, null, null, Now);

        session.StartedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Dado_Aggregate_Quando_Upsert_Entao_SomaContadores()
    {
        var agg = CliDailyUsageAggregate.Register(AgentCliKind.OpenCode, "2026-09-19", Now);
        agg.Add(sessions: 2, messages: 10, tokensIn: 100, tokensOut: 50, tokensCached: 5, "gpt-x", Now);
        agg.Add(sessions: 1, messages: 3, tokensIn: 40, tokensOut: 10, tokensCached: 0, "gpt-y", Now);

        agg.SessionsCount.ShouldBe(3);
        agg.MessagesCount.ShouldBe(13);
        agg.TokensInput.ShouldBe(140);
        agg.TokensOutput.ShouldBe(60);
        agg.ModelsUsed.ShouldBe(["gpt-x", "gpt-y"]);
    }
}
