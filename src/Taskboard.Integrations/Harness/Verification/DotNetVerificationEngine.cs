using System.Text;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Harness.Verification;

/// <summary>
/// Sequential verification pipeline inside an agent worktree
/// (SPEC-20260919-harness-verification-loop RF-001..RF-005):
/// format (optional) → build (warnings-as-errors) → test (trx + coverage) →
/// coverage ratchet. Produces the structured report and the markdown feedback
/// prompt reinjected into the agent on failure.
/// </summary>
public sealed class DotNetVerificationEngine : IVerificationEngine
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(120);

    private readonly IProcessRunner _runner;

    public DotNetVerificationEngine(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<VerificationReportDto> RunAsync(
        VerificationRunRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var solution = Path.GetFullPath(request.SolutionFile, request.WorktreePath);

        // 1. Format (opt-in).
        if (request.EnforceFormat)
        {
            var format = await _runner.RunAsync(
                "dotnet", request.WorktreePath,
                ["format", solution, "--verify-no-changes"],
                BuildTimeout, cancellationToken);
            if (format.ExitCode != 0)
            {
                return Report(request, VerificationStatus.FormatFailed, [],
                    null, -1, "Arquivos fora do padrão de formatação.");
            }
        }

        // 2. Build — warnings as errors (RF-001).
        var build = await _runner.RunAsync(
            "dotnet", request.WorktreePath,
            ["build", solution, "--configuration", "Release", "-p:TreatWarningsAsErrors=true"],
            BuildTimeout, cancellationToken);
        if (build.TimedOut || build.ExitCode != 0)
        {
            var errors = CompilerErrorParser.Parse(build.StdOut + "\n" + build.StdErr);
            return Report(request, VerificationStatus.BuildFailed, errors, null, -1, null);
        }

        // 3. Tests — trx + XPlat coverage (RF-002).
        var resultsDir = Path.Combine(request.WorktreePath, "TestResults");
        var test = await _runner.RunAsync(
            "dotnet", request.WorktreePath,
            ["test", solution, "--no-build", "--configuration", "Release",
             "--logger", "trx;LogFileName=results.trx",
             "--collect", "XPlat Code Coverage",
             "--results-directory", resultsDir],
            TestTimeout, cancellationToken);

        if (test.TimedOut)
        {
            return Report(request, VerificationStatus.TestTimeout, [], null, -1,
                "A suíte de testes excedeu o timeout de 120s.");
        }

        var summary = ParseNewestTrx(request.WorktreePath);
        if (test.ExitCode != 0 || summary is { Failed: > 0 })
        {
            return Report(request, VerificationStatus.TestsFailed, [],
                summary ?? new TestSummaryDto(0, 0, 1, []), -1, null);
        }

        // 4. Coverage ratchet (RF-003). Missing collector file → warn & skip gate.
        var coverage = ParseNewestCoverage(request.WorktreePath);
        if (coverage is { } measured && measured < request.MinCoverageThreshold)
        {
            return Report(request, VerificationStatus.CoverageRegression, [], summary,
                measured,
                $"Cobertura {measured:F2}% abaixo do ratchet {request.MinCoverageThreshold:F2}%.");
        }

        return new VerificationReportDto(
            true, nameof(VerificationStatus.Passed), [], summary, coverage ?? 0.0, null);
    }

    private TestSummaryDto? ParseNewestTrx(string worktreePath)
    {
        var trx = NewestFile(worktreePath, "results.trx");
        return trx is null ? null : TestFailureParser.Parse(File.ReadAllText(trx));
    }

    private static double? ParseNewestCoverage(string worktreePath)
    {
        var file = NewestFile(worktreePath, "coverage.cobertura.xml");
        return file is null ? null : CoverageCalculator.ParseLineRate(File.ReadAllText(file));
    }

    private static string? NewestFile(string worktreePath, string name)
        => Directory.Exists(worktreePath)
            ? Directory.EnumerateFiles(worktreePath, name, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

    private static VerificationReportDto Report(
        VerificationRunRequestDto request,
        VerificationStatus status,
        IReadOnlyList<CompilationErrorDto> errors,
        TestSummaryDto? summary,
        double coverage,
        string? reason)
        => new(
            false, status.ToString(), errors, summary, coverage,
            BuildFeedbackPrompt(request, status, errors, summary, reason));

    /// <summary>RF-004 — markdown enxuto reinjetado no contexto do agente.</summary>
    internal static string BuildFeedbackPrompt(
        VerificationRunRequestDto request,
        VerificationStatus status,
        IReadOnlyList<CompilationErrorDto> errors,
        TestSummaryDto? summary,
        string? reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## ❌ Verificação Falhou (Tentativa {request.Attempt}/{request.MaxAttempts})");
        sb.AppendLine();
        sb.AppendLine($"**Status:** `{status}`");
        if (reason is not null)
        {
            sb.AppendLine($"**Motivo:** {reason}");
        }

        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Erros de Compilação:");
            foreach (var e in errors.Take(20))
            {
                sb.AppendLine($"- `{e.File}({e.Line},{e.Column})`: {e.ErrorCode}: {e.Message}");
            }
        }

        if (summary?.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Testes Falhando:");
            foreach (var f in summary.Failures.Take(20))
            {
                sb.AppendLine($"- `{f.Name}`: {f.Message.Split('\n')[0]}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Por favor, corrija estes arquivos diretamente no seu workspace.");
        return sb.ToString();
    }
}
