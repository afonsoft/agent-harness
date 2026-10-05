using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Taskboard.Application.Contracts.Skills;
using Taskboard.Integrations.Agents;

namespace Taskboard.Integrations.Skills;

/// <summary>
/// Skills.eco search — <c>npx skills find &lt;q&gt;</c> output parsed into rows
/// (SPEC-20261010-mcp-skills-hub RF-005). The query goes through argv (never a
/// shell); output is capped at 20 results. Parser is tolerant: lines that do
/// not match the <c>repo[@skill]</c>/<c>name …</c> shapes degrade to name rows.
/// </summary>
public sealed class SkillsSearchService
{
    internal const int MaxResults = 20;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);

    // "owner/repo" or "owner/repo@skill" tokens inside a result line.
    private static readonly Regex RepoToken = new(
        @"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:@[a-z0-9-_]{1,64})?",
        RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly ISkillsInstallRunner _runner;
    private readonly Func<string, string?> _locator;
    private readonly ILogger<SkillsSearchService> _logger;

    public SkillsSearchService(
        ILogger<SkillsSearchService> logger,
        ISkillsInstallRunner? runner = null,
        Func<string, string?>? executableLocator = null)
    {
        _logger = logger;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
        _locator = executableLocator ?? PathSearch.FindExecutable;
    }

    /// <summary>
    /// Runs <c>npx skills find &lt;query&gt;</c>. Throws
    /// <see cref="InvalidOperationException"/> when npx is unavailable —
    /// callers map it to a 503-style error.
    /// </summary>
    public async Task<IReadOnlyList<SkillSearchResultDto>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        var npx = _locator("npx")
            ?? throw new InvalidOperationException("PrerequisiteMissing: npx");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SearchTimeout);
        CommandResult result;
        try
        {
            result = await _runner
                .RunAsync(npx, Environment.CurrentDirectory, ["skills", "find", query], timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("skills search timed out after 20s");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"skills find failed (exit {result.ExitCode}): {Trim(result.StdErr)}");
        }

        return Parse(result.StdOut);
    }

    /// <summary>
    /// Tolerant parser: every line carrying an <c>owner/repo[@skill]</c> token
    /// becomes a result; other non-empty lines degrade to name rows (the
    /// output format of <c>skills find</c> is not versioned).
    /// </summary>
    internal static IReadOnlyList<SkillSearchResultDto> Parse(string output)
    {
        var results = new List<SkillSearchResultDto>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')
                || line.StartsWith("Searching", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Found", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Install", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Usage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = RepoToken.Match(line);
            if (!match.Success)
            {
                // Unrecognized shape — keep the line as a name row so the UI
                // still shows something actionable.
                results.Add(new SkillSearchResultDto(line, string.Empty, null, null));
            }
            else
            {
                var target = match.Value;
                var hasSkill = target.Contains('@');
                var name = hasSkill
                    ? target[(target.IndexOf('@') + 1)..]
                    : target[(target.IndexOf('/') + 1)..];
                var description = line.Replace(target, string.Empty).Trim(' ', '·', '-', '—', ':');
                results.Add(new SkillSearchResultDto(
                    name,
                    Repository: hasSkill ? target[..target.IndexOf('@')] : target,
                    Skill: hasSkill ? name : null,
                    Description: description.Length == 0 ? null : description));
            }

            if (results.Count >= MaxResults)
            {
                break;
            }
        }

        return results;
    }

    private static string Trim(string? output)
    {
        var text = output?.Trim() ?? string.Empty;
        return text.Length <= 300 ? text : text[..300];
    }
}
