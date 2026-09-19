using Shouldly;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;
using Taskboard.Integrations.Harness.Verification;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-verification-loop — orquestração format/build/test/coverage.</summary>
public class DotNetVerificationEngineTests : IDisposable
{
    private readonly string _worktree;
    private readonly FakeRunner _runner = new();
    private readonly DotNetVerificationEngine _sut;

    public DotNetVerificationEngineTests()
    {
        _worktree = Path.Combine(Path.GetTempPath(), $"tb-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_worktree);
        File.WriteAllText(Path.Combine(_worktree, "Taskboard.sln"), "fake-sln");
        _sut = new DotNetVerificationEngine(_runner);
    }

    public void Dispose() => Directory.Delete(_worktree, recursive: true);

    private VerificationRunRequestDto Request(double threshold = 66.0) =>
        new(_worktree, "Taskboard.sln", threshold);

    private sealed class FakeRunner : IProcessRunner
    {
        public Func<string, IReadOnlyList<string>, ProcessRunResult> Handler { get; set; }
            = (_, _) => new ProcessRunResult(0, "", "", false);
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            string executable, string workingDirectory, IReadOnlyList<string> arguments,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            return Task.FromResult(Handler(executable, arguments));
        }
    }

    private void WriteTrx(string trx = """
        <?xml version="1.0"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="T1" outcome="Passed" /></Results>
          <ResultSummary outcome="Completed"><Counters total="1" passed="1" failed="0" /></ResultSummary>
        </TestRun>
        """, double lineRate = 0.70)
    {
        var dir = Path.Combine(_worktree, "TestResults", "run1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "results.trx"), trx);
        File.WriteAllText(Path.Combine(dir, "coverage.cobertura.xml"),
            $"<?xml version=\"1.0\"?><coverage line-rate=\"{lineRate}\"><packages /></coverage>");
    }

    [Fact]
    public async Task Dado_TudoVerde_Quando_Run_Entao_Passed()
    {
        _runner.Handler = (_, args) => new ProcessRunResult(0, "Build succeeded.", "", false);
        WriteTrx();

        var report = await _sut.RunAsync(Request());

        report.IsSuccess.ShouldBeTrue();
        report.Status.ShouldBe(nameof(VerificationStatus.Passed));
        report.CoveragePercent.ShouldBe(70.0, tolerance: 0.1);
        report.FeedbackPrompt.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_BuildFalha_Quando_Run_Entao_BuildFailedComErrosEPrompt()
    {
        _runner.Handler = (_, args) => args[0] == "build"
            ? new ProcessRunResult(1,
                "src/Program.cs(42,10): error CS0246: The type or namespace name 'X' could not be found [/r/p.csproj]\nBuild FAILED.",
                "", false)
            : new ProcessRunResult(0, "", "", false);

        var report = await _sut.RunAsync(Request());

        report.IsSuccess.ShouldBeFalse();
        report.Status.ShouldBe(nameof(VerificationStatus.BuildFailed));
        report.CompilationErrors.ShouldContain(e => e.ErrorCode == "CS0246" && e.Line == 42);
        report.FeedbackPrompt.ShouldNotBeNull().ShouldContain("CS0246");
        report.TestSummary.ShouldBeNull(); // testes não rodam se o build quebra
    }

    [Fact]
    public async Task Dado_TesteFalha_Quando_Run_Entao_TestsFailedComNomes()
    {
        _runner.Handler = (_, _) => new ProcessRunResult(0, "", "", false);
        _runner.Handler = (_, args) => args[0] == "test"
            ? new ProcessRunResult(1, "Failed!  - Failed: 1", "", false)
            : new ProcessRunResult(0, "Build succeeded.", "", false);
        WriteTrx("""
            <?xml version="1.0"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Ns.FailingTest" outcome="Failed">
                  <Output><ErrorInfo><Message>Expected 1 but was 2</Message></ErrorInfo></Output>
                </UnitTestResult>
              </Results>
              <ResultSummary outcome="Failed"><Counters total="1" passed="0" failed="1" /></ResultSummary>
            </TestRun>
            """);

        var report = await _sut.RunAsync(Request());

        report.Status.ShouldBe(nameof(VerificationStatus.TestsFailed));
        report.TestSummary.ShouldNotBeNull().Failures.ShouldContain(f => f.Name == "Ns.FailingTest");
        report.FeedbackPrompt.ShouldNotBeNull().ShouldContain("Ns.FailingTest");
    }

    [Fact]
    public async Task Dado_CoberturaAbaixoDoRatchet_Quando_Run_Entao_CoverageRegression()
    {
        _runner.Handler = (_, _) => new ProcessRunResult(0, "ok", "", false);
        WriteTrx(lineRate: 0.50);

        var report = await _sut.RunAsync(Request(threshold: 66.26));

        report.Status.ShouldBe(nameof(VerificationStatus.CoverageRegression));
        report.IsSuccess.ShouldBeFalse();
        report.CoveragePercent.ShouldBe(50.0, tolerance: 0.1);
    }

    [Fact]
    public async Task Dado_TimeoutNosTestes_Quando_Run_Entao_TestTimeout()
    {
        _runner.Handler = (_, args) => args[0] == "test"
            ? new ProcessRunResult(-1, "", "", TimedOut: true)
            : new ProcessRunResult(0, "ok", "", false);

        var report = await _sut.RunAsync(Request());

        report.Status.ShouldBe(nameof(VerificationStatus.TestTimeout));
        report.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SemArquivoCobertura_Quando_Run_Entao_UsaTestesSemQuebrar()
    {
        _runner.Handler = (_, _) => new ProcessRunResult(0, "ok", "", false);
        var dir = Path.Combine(_worktree, "TestResults", "run1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "results.trx"), """
            <?xml version="1.0"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed"><Counters total="3" passed="3" failed="0" /></ResultSummary>
            </TestRun>
            """);

        var report = await _sut.RunAsync(Request());

        report.IsSuccess.ShouldBeTrue();
        report.Status.ShouldBe(nameof(VerificationStatus.Passed));
        report.CoveragePercent.ShouldBe(0.0); // N/A — gate não aplicado
    }

    [Fact]
    public async Task Dado_EnforceFormat_Quando_FormatFalha_Entao_FormatFailed()
    {
        _runner.Handler = (_, args) => args[0] == "format"
            ? new ProcessRunResult(1, "file.cs needs formatting", "", false)
            : new ProcessRunResult(0, "ok", "", false);

        var report = await _sut.RunAsync(Request() with { EnforceFormat = true });

        report.Status.ShouldBe(nameof(VerificationStatus.FormatFailed));
        report.IsSuccess.ShouldBeFalse();
    }
}
