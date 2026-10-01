using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>SPEC-20260929-jobs-dashboard RF-001/RF-003/RF-004 — loop do ManagedJobService.</summary>
public sealed class ManagedJobServiceTests
{
    private const string Key = "test-job";

    private static JobDefinition Def(int interval = 3600, bool runOnce = false, bool enabled = true) =>
        new(Key, "test", "", interval, 1, runOnce, enabled);

    private sealed class TestJob : ManagedJobService
    {
        public int Runs;
        public Func<CancellationToken, Task<string?>>? OnRun;

        public TestJob(JobRegistry registry)
            : base(registry, Key, NullLogger<TestJob>.Instance)
        {
        }

        protected override Task<string?> RunJobAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            return OnRun?.Invoke(cancellationToken) ?? Task.FromResult<string?>("ok");
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Dado_JobEnabled_Quando_StartAsync_Entao_KickInicialExecuta()
    {
        var registry = JobRegistryTestHost.Create(definitions: Def());
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        await WaitForAsync(() => job.Runs > 0);
        await job.StopAsync(CancellationToken.None);

        job.Runs.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Dado_JobDisabled_Quando_StartAsync_Entao_NaoExecuta()
    {
        var registry = JobRegistryTestHost.Create(definitions: Def(enabled: false));
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await job.StopAsync(CancellationToken.None);

        job.Runs.ShouldBe(0);
        registry.GetStatuses().Single().Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_JobDisabled_Quando_OverrideHabilita_Entao_TickExecutaSemRestart()
    {
        var registry = JobRegistryTestHost.Create(definitions: Def(interval: 3600, enabled: false));
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(200);
            job.Runs.ShouldBe(0);

            var result = await registry.SetOverrideAsync(Key, enabled: true, intervalSeconds: 1);
            result.Error.ShouldBe(JobUpdateError.None);

            await WaitForAsync(() => job.Runs > 0);
            job.Runs.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_IntervalOverride_Quando_SetOverride_Entao_LoopUsaNovoIntervalo()
    {
        // Default 1h; override para 1s — o loop deve acordar e tickar no novo intervalo.
        var registry = JobRegistryTestHost.Create(definitions: Def(interval: 3600));
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(() => job.Runs >= 1); // kick inicial

            var result = await registry.SetOverrideAsync(Key, enabled: null, intervalSeconds: 1);
            result.Error.ShouldBe(JobUpdateError.None);

            await WaitForAsync(() => job.Runs >= 2);
            job.Runs.ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_IntervaloLongo_Quando_Trigger_Entao_ExecutaImediato()
    {
        var registry = JobRegistryTestHost.Create(definitions: Def(interval: 3600));
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(() => job.Runs >= 1); // kick inicial
            var before = job.Runs;

            registry.Trigger(Key).ShouldBe(JobTriggerResult.Started);

            await WaitForAsync(() => job.Runs > before);
            job.Runs.ShouldBeGreaterThan(before);
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_SinalDeScheduleChanged_Quando_LoopAguarda_Entao_ReaplicaSemRunExtra()
    {
        // B-04: ScheduleChanged acorda o loop para reler o schedule sem disparar
        // run — o reader do canal deve estar vivo independente de quem venceu
        // o WhenAny anterior.
        var registry = JobRegistryTestHost.Create(definitions: Def(interval: 3600, enabled: false));
        var job = new TestJob(registry);

        await job.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(200);
            job.Runs.ShouldBe(0);

            var result = await registry.SetOverrideAsync(Key, enabled: true, intervalSeconds: 3600);
            result.Error.ShouldBe(JobUpdateError.None);

            // O wake deve ser processado — sem run (ScheduleChanged != RunRequested).
            await Task.Delay(300);
            job.Runs.ShouldBe(0);

            registry.Trigger(Key).ShouldBe(JobTriggerResult.Started);
            await WaitForAsync(() => job.Runs >= 1);
            job.Runs.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_RunBloqueado_Quando_TriggerDeNovo_Entao_AlreadyRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = JobRegistryTestHost.Create(definitions: Def(interval: 3600));
        var job = new TestJob(registry)
        {
            OnRun = async _ =>
            {
                started.TrySetResult();
                await release.Task;
                return "blocked";
            }
        };

        await job.StartAsync(CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            registry.Trigger(Key).ShouldBe(JobTriggerResult.AlreadyRunning);
            registry.TryBeginRun(Key).ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            await job.StopAsync(CancellationToken.None);
        }

        job.Runs.ShouldBe(1);
    }
}
