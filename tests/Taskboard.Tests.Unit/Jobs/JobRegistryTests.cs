using Shouldly;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>SPEC-20260929-jobs-dashboard RF-002/RF-003 — registry, overrides, ring buffer.</summary>
public sealed class JobRegistryTests
{
    private static JobDefinition Def(string key, int interval = 3600, int min = 60, bool runOnce = false, bool enabled = true) =>
        new(key, $"name-{key}", $"desc-{key}", interval, min, runOnce, enabled);

    [Fact]
    public void Dado_Definicoes_Quando_GetStatuses_Entao_DefaultsAplicados()
    {
        var registry = JobRegistryTestHost.Create(definitions: [Def("b-job"), Def("a-job", runOnce: true)]);

        var statuses = registry.GetStatuses();

        statuses.Count.ShouldBe(2);
        statuses[0].Key.ShouldBe("a-job"); // ordenado por key
        statuses[0].RunOnce.ShouldBeTrue();
        statuses[1].Enabled.ShouldBeTrue();
        statuses[1].IntervalSeconds.ShouldBe(3600);
        statuses[1].IsRunning.ShouldBeFalse();
        statuses[1].RunCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_OverridePersistido_Quando_GetEffective_Entao_OverrideVence()
    {
        var store = new InMemoryJobScheduleStore();
        var registry = JobRegistryTestHost.Create(store, Def("job-a", interval: 3600));

        var result = await registry.SetOverrideAsync("job-a", enabled: false, intervalSeconds: 120);
        result.Error.ShouldBe(JobUpdateError.None);
        store.Upserts.ShouldBe(1);

        var effective = await registry.GetEffectiveAsync("job-a");
        effective.Enabled.ShouldBeFalse();
        effective.Interval.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task Dado_JobDesconhecido_Quando_SetOverride_Entao_UnknownJob()
    {
        var registry = JobRegistryTestHost.Create(definitions: [Def("job-a")]);

        var result = await registry.SetOverrideAsync("nope", enabled: true, intervalSeconds: null);

        result.Error.ShouldBe(JobUpdateError.UnknownJob);
        result.Status.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_IntervaloAbaixoDoMin_Quando_SetOverride_Entao_IntervalOutOfRange()
    {
        var registry = JobRegistryTestHost.Create(definitions: [Def("job-a", min: 300)]);

        var result = await registry.SetOverrideAsync("job-a", enabled: null, intervalSeconds: 60);

        result.Error.ShouldBe(JobUpdateError.IntervalOutOfRange);
        result.Status.ShouldBeNull();
        (await registry.GetEffectiveAsync("job-a")).Interval.ShouldBe(TimeSpan.FromSeconds(3600));
    }

    [Fact]
    public void Dado_JobDesconhecido_Quando_Trigger_Entao_UnknownJob()
    {
        var registry = JobRegistryTestHost.Create();

        registry.Trigger("nope").ShouldBe(JobTriggerResult.UnknownJob);
    }

    [Fact]
    public void Dado_RunEmVoo_Quando_TryBeginRunDeNovo_Entao_SingleFlight()
    {
        var registry = JobRegistryTestHost.Create(definitions: [Def("job-a")]);

        registry.TryBeginRun("job-a").ShouldBeTrue();
        registry.TryBeginRun("job-a").ShouldBeFalse();
        registry.Trigger("job-a").ShouldBe(JobTriggerResult.AlreadyRunning);

        registry.ReportFinished("job-a", true, "done");

        registry.TryBeginRun("job-a").ShouldBeTrue();
        var status = registry.GetStatuses().Single(s => s.Key == "job-a");
        status.IsRunning.ShouldBeTrue();
        status.RunCount.ShouldBe(2);
    }

    [Fact]
    public void Dado_MuitasExecucoes_Quando_ReportFinished_Entao_LogCapEm50()
    {
        var registry = JobRegistryTestHost.Create(definitions: [Def("job-a")]);

        for (var i = 0; i < 60; i++)
        {
            registry.TryBeginRun("job-a");
            registry.ReportFinished("job-a", i % 2 == 0, $"run {i}");
        }

        var status = registry.GetStatuses().Single();
        status.Log.Count.ShouldBeLessThanOrEqualTo(50);
        status.RunCount.ShouldBe(60);
        status.LastOutcome.ShouldBe("error"); // run 59 é ímpar → error
        status.LastMessage.ShouldBe("run 59");
    }
}
