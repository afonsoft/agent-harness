using System.Text.RegularExpressions;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Harness.Verification;

/// <summary>
/// Extracts structured diagnostics from `dotnet build` output (RF-001).
/// MSBuild format: <c>path(line,col): error|warning CODE: message [project]</c>.
/// With <c>TreatWarningsAsErrors</c> warnings are also failures, so both are
/// captured.
/// </summary>
public sealed partial class CompilerErrorParser
{
    public static IReadOnlyList<CompilationErrorDto> Parse(string buildOutput)
    {
        if (string.IsNullOrEmpty(buildOutput))
        {
            return [];
        }

        var errors = new List<CompilationErrorDto>();
        foreach (Match match in DiagnosticLineRegex().Matches(buildOutput))
        {
            errors.Add(new CompilationErrorDto(
                match.Groups["file"].Value,
                int.Parse(match.Groups["line"].Value),
                int.Parse(match.Groups["col"].Value),
                match.Groups["code"].Value,
                match.Groups["msg"].Value.Trim()));
        }

        return errors;
    }

    [GeneratedRegex(
        @"^(?<file>[^\r\n(]+)\((?<line>\d+),(?<col>\d+)\):\s*(?:error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.+?)(?:\s*\[[^\]]+\])?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex DiagnosticLineRegex();
}
