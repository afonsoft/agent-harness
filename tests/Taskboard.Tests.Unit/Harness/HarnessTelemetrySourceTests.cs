using System.Diagnostics;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public sealed class HarnessTelemetrySourceTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public HarnessTelemetrySourceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HarnessTelemetrySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Dado_RunSpan_Quando_ListenerAtivo_Entao_TagsObrigatorias()
    {
        using (HarnessTelemetrySource.StartRunSpan("run-1", AgentType.Claude, "claude-3-7-sonnet"))
        {
        }

        var activity = _activities.ShouldHaveSingleItem();
        activity.OperationName.ShouldBe("harness.run");
        activity.GetTagItem(HarnessTelemetrySource.RunIdTag).ShouldBe("run-1");
        activity.GetTagItem(HarnessTelemetrySource.AgentTypeTag).ShouldBe("Claude");
        activity.GetTagItem(HarnessTelemetrySource.ModelNameTag).ShouldBe("claude-3-7-sonnet");
    }

    [Fact]
    public void Dado_SpansFilhos_Quando_ListenerAtivo_Entao_RunIdEAgentPresentes()
    {
        // AC3: metadados de runId e agent em todos os spans filhos.
        using (var run = HarnessTelemetrySource.StartRunSpan("run-9", AgentType.Codex, "gpt-5"))
        {
            using (HarnessTelemetrySource.StartStageSpan("run-9", "plan", AgentType.Codex, "gpt-5"))
            {
            }

            using (HarnessTelemetrySource.StartVerificationSpan("run-9", AgentType.Codex, "gpt-5"))
            {
            }
        }

        _activities.Count.ShouldBe(3);
        foreach (var activity in _activities)
        {
            activity.GetTagItem(HarnessTelemetrySource.RunIdTag).ShouldBe("run-9");
            activity.GetTagItem(HarnessTelemetrySource.AgentTypeTag).ShouldBe("Codex");
        }
    }

    [Fact]
    public void Dado_Usage_Quando_RecordUsage_Entao_TagsDeTokensECusto()
    {
        using (var run = HarnessTelemetrySource.StartRunSpan("run-2", AgentType.Claude, null))
        {
            HarnessTelemetrySource.RecordUsage(run, new TokenUsage(1000, 500, 100, 50), 0.01m);
        }

        var activity = _activities.ShouldHaveSingleItem();
        activity.GetTagItem(HarnessTelemetrySource.TokensTotalTag).ShouldBe(1650L);
        activity.GetTagItem(HarnessTelemetrySource.CostUsdTag).ShouldBe(0.01m);
    }

    [Fact]
    public void Dado_SemListener_Quando_StartSpan_Entao_RetornaNull()
    {
        _listener.Dispose();
        // Sem listener, StartActivity retorna null — custo zero.
        HarnessTelemetrySource.StartRunSpan("x", AgentType.Codex, null).ShouldBeNull();
    }
}
