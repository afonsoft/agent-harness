using Shouldly;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Verification;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-verification-loop RF-005 — loop de correção + escalonamento.</summary>
public class VerificationLoopTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeRepo _repo = new();
    private readonly VerificationLoop _sut;

    public VerificationLoopTests()
    {
        _sut = new VerificationLoop(_engine, _repo);
    }

    private sealed class FakeEngine : Taskboard.Application.Contracts.Harness.IVerificationEngine
    {
        public Queue<VerificationReportDto> Results { get; } = new();

        public Task<VerificationReportDto> RunAsync(
            VerificationRunRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Passed());
    }

    private sealed class FakeRepo : Taskboard.Application.Contracts.Harness.IVerificationReportRepository
    {
        public List<VerificationReportDto> Saved { get; } = [];

        public Task SaveAsync(string worktreePath, VerificationReportDto report, int attempts,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(report);
            return Task.CompletedTask;
        }
    }

    private static VerificationReportDto Passed() =>
        new(true, nameof(VerificationStatus.Passed), [], null, 70.0, null);

    private static VerificationReportDto Failed(string status) =>
        new(false, status, [], null, 0.0, "## ❌ Verificação Falhou");

    private static VerificationRunRequestDto Request(int maxAttempts = 3) =>
        new("/tmp/wt", "App.sln", 66.0, MaxAttempts: maxAttempts);

    [Fact]
    public async Task Dado_VerdeNaPrimeira_Quando_Run_Entao_SemRetryEPersistido()
    {
        _engine.Results.Enqueue(Passed());
        var retries = 0;

        var report = await _sut.RunAsync(Request(), (_, _) => { retries++; return Task.CompletedTask; }, default);

        report.IsSuccess.ShouldBeTrue();
        retries.ShouldBe(0);
        _repo.Saved.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_FalhaDepoisVerde_Quando_Run_Entao_ReinvocaComFeedbackPrompt()
    {
        _engine.Results.Enqueue(Failed("BuildFailed"));
        _engine.Results.Enqueue(Passed());
        var prompts = new List<string>();

        var report = await _sut.RunAsync(Request(),
            (p, _) => { prompts.Add(p); return Task.CompletedTask; }, default);

        report.IsSuccess.ShouldBeTrue();
        prompts.Count.ShouldBe(1);
        prompts[0].ShouldContain("Verificação Falhou");
        _repo.Saved.Count.ShouldBe(2);
    }

    // AC-05: esgotadas as tentativas → EscalatedToHuman, sem nova invocação.
    [Fact]
    public async Task Dado_FalhasAteOLimite_Quando_Run_Entao_EscalatedToHuman()
    {
        _engine.Results.Enqueue(Failed("BuildFailed"));
        _engine.Results.Enqueue(Failed("TestsFailed"));
        _engine.Results.Enqueue(Failed("TestsFailed"));
        var retries = 0;

        var report = await _sut.RunAsync(Request(maxAttempts: 3),
            (_, _) => { retries++; return Task.CompletedTask; }, default);

        report.Status.ShouldBe(nameof(VerificationStatus.EscalatedToHuman));
        report.IsSuccess.ShouldBeFalse();
        retries.ShouldBe(2); // maxAttempts-1 retries
        _repo.Saved.Last().Status.ShouldBe(nameof(VerificationStatus.EscalatedToHuman));
        _repo.Saved.Count.ShouldBe(4); // 3 tentativas + 1 escalated
    }
}
