using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Dtos;
using Taskboard.Specs;

namespace Taskboard.Application.Specs;

/// <summary>
/// File-backed living-spec catalog — scans <c>.specs/</c>, parses every
/// <c>SPEC-*.md</c> and rewrites the Status metadata cell in place on updates
/// (SPEC-20260919-ade-living-specs §4, guardrail §8: never rewrite free-form
/// sections, preserve UTF-8). SPEC-20260920 RF-005: the specs dir is resolved
/// per request — <c>repo</c> → <c>~/repos/&lt;name&gt;/.specs</c>, otherwise
/// the configured default dir.
/// </summary>
public sealed partial class SpecAppService : ISpecAppService
{
    private readonly ISpecDocumentParser _parser;
    private readonly string? _configuredSpecsDir;
    private readonly IWorkspacePathResolver _workspace;
    private readonly ILogger<SpecAppService> _logger;

    public SpecAppService(
        ISpecDocumentParser parser,
        IConfiguration configuration,
        IWorkspacePathResolver workspace,
        ILogger<SpecAppService> logger)
    {
        _parser = parser;
        _workspace = workspace;
        _logger = logger;
        _configuredSpecsDir = configuration["Taskboard:SpecsDir"];
    }

    public Task<IReadOnlyList<LivingSpecDto>> ListAsync(
        string? status, string? query, string? repo = null, CancellationToken cancellationToken = default)
    {
        var specs = ScanSpecs(ResolveSpecsDir(repo))
            .Where(s => MatchesStatus(s, status))
            .Where(s => MatchesQuery(s, query))
            .OrderByDescending(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
        return Task.FromResult<IReadOnlyList<LivingSpecDto>>(specs);
    }

    public Task<LivingSpecDetailDto?> GetAsync(
        string specId, string? repo = null, CancellationToken cancellationToken = default)
    {
        var specsDir = ResolveSpecsDir(repo);
        var file = FindSpecFile(specsDir, specId);
        if (file is null)
        {
            return Task.FromResult<LivingSpecDetailDto?>(null);
        }
        var markdown = File.ReadAllText(file);
        return Task.FromResult<LivingSpecDetailDto?>(
            ToDetailDto(_parser.Parse(file, markdown), markdown, specsDir));
    }

    public async Task<LivingSpecDetailDto?> UpdateStatusAsync(
        string specId, string status, string? repo = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SpecStatus>(status, ignoreCase: true, out var parsed))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidSpecStatus,
                $"Unknown spec status '{status}'. Valid: Draft, Approved, InImplementation, Done, Deprecated.");
        }

        var specsDir = ResolveSpecsDir(repo);
        var file = FindSpecFile(specsDir, specId);
        if (file is null)
        {
            return null;
        }

        var markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var rewritten = StatusRowRegex().Replace(
            markdown,
            m => $"{m.Groups[1].Value} `{parsed}` |",
            count: 1);

        if (ReferenceEquals(rewritten, markdown) || rewritten == markdown)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidSpecStatus,
                $"Spec '{specId}' has no Status metadata row to update.");
        }

        // Preserve UTF-8 integrity — specs are BOM-less UTF-8 across the corpus.
        await File.WriteAllTextAsync(file, rewritten, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Spec {SpecId} status updated to {Status}", specId, parsed);
        return await GetAsync(specId, repo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20260920 RF-005: <paramref name="repo"/> → the clone's
    /// <c>.specs</c> (only when both the clone and the dir exist); absent →
    /// the configured dir / app-base walk-up. Missing dir → null → empty catalog.
    /// </summary>
    private string? ResolveSpecsDir(string? repo)
    {
        if (!string.IsNullOrWhiteSpace(repo))
        {
            var workdir = _workspace.ResolveCardWorkdir(repo, out var cloneExists);
            if (!cloneExists)
            {
                return null;
            }
            var specsDir = Path.Combine(workdir, ".specs");
            return Directory.Exists(specsDir) ? specsDir : null;
        }
        return ResolveDefaultSpecsDir(_configuredSpecsDir);
    }

    internal IReadOnlyList<LivingSpecification> ScanSpecs(string? specsDir)
    {
        if (specsDir is null || !Directory.Exists(specsDir))
        {
            return [];
        }

        var specs = new List<LivingSpecification>();
        foreach (var file in Directory.EnumerateFiles(specsDir, "SPEC-*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                specs.Add(_parser.Parse(file, File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to parse spec file {File}", file);
                specs.Add(new LivingSpecification(
                    Path.GetFileNameWithoutExtension(file), file,
                    Path.GetFileNameWithoutExtension(file),
                    null, null, null, null, SpecStatus.Draft, null, null,
                    [], [], [], [],
                    [new SpecLintWarning("PARSE_ERROR", ex.Message)]));
            }
        }
        return specs;
    }

    private static string? FindSpecFile(string? specsDir, string specId)
    {
        if (specsDir is null || specId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }
        var candidate = Path.Combine(specsDir, specId + ".md");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveDefaultSpecsDir(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".specs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static bool MatchesStatus(LivingSpecification spec, string? status) =>
        string.IsNullOrWhiteSpace(status)
        || spec.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesQuery(LivingSpecification spec, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || spec.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || spec.Title.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static LivingSpecDto ToDto(LivingSpecification s) =>
        new(
            s.Id, s.Title, s.Type, s.Status.ToString(), s.RawStatus,
            s.Date?.ToString("yyyy-MM-dd"), s.Ticket,
            s.Requirements.Count, s.AcceptanceCriteria.Count,
            s.Tasks.Count, s.Tasks.Count(t => t.Done),
            s.Warnings.Select(w => $"{w.Code}: {w.Message}").ToList());

    private static LivingSpecDetailDto ToDetailDto(LivingSpecification s, string markdown, string? specsDir) =>
        new(
            s.Id, s.Title, s.Type, s.Status.ToString(), s.RawStatus,
            s.Date?.ToString("yyyy-MM-dd"), s.Ticket, s.Branch,
            specsDir is null ? null : Directory.GetParent(specsDir)?.FullName,
            s.Requirements.Select(r => new SpecRequirementDto(r.Code, r.Title)).ToList(),
            s.AcceptanceCriteria,
            s.Tasks.Select(t => new SpecTaskDto(t.Title, t.Done)).ToList(),
            s.ReferencedFiles,
            s.Warnings.Select(w => $"{w.Code}: {w.Message}").ToList(),
            markdown);

    [GeneratedRegex(@"^(\|\s*Status\s*\|)([^|]*)(\|\s*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex StatusRowRegex();
}
