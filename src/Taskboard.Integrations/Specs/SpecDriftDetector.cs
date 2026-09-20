using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Dtos;
using Taskboard.Specs;

namespace Taskboard.Integrations.Specs;

/// <summary>
/// File-existence drift detector (SPEC-20260919-ade-living-specs RF-002):
/// a spec in Draft/Approved/InImplementation whose "Files to create or
/// modify" all exist on disk is stale — suggest Done. A Done spec whose
/// referenced files were since deleted drifts the other way — suggest
/// Deprecated. Repo root is the parent of the resolved <c>.specs</c> dir.
/// </summary>
public sealed class SpecDriftDetector : ISpecDriftDetector
{
    private readonly ISpecDocumentParser _parser;
    private readonly string? _specsDir;
    private readonly string? _repoRoot;

    public SpecDriftDetector(ISpecDocumentParser parser, IConfiguration configuration)
    {
        _parser = parser;
        _specsDir = ResolveSpecsDir(configuration["Taskboard:SpecsDir"]);
        _repoRoot = _specsDir is null ? null : Directory.GetParent(_specsDir)?.FullName;
    }

    public Task<SpecDriftReportDto> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        if (_specsDir is null || _repoRoot is null || !Directory.Exists(_specsDir))
        {
            return Task.FromResult(new SpecDriftReportDto(0, 0, []));
        }

        var items = new List<SpecDriftItemDto>();
        var total = 0;
        foreach (var file in Directory.EnumerateFiles(_specsDir, "SPEC-*.md"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            LivingSpecification spec;
            try
            {
                spec = _parser.Parse(file, File.ReadAllText(file));
            }
            catch
            {
                continue; // parse anomalies surface as warnings in the catalog, not drift
            }

            if (spec.ReferencedFiles.Count == 0)
            {
                continue;
            }

            var missing = spec.ReferencedFiles
                .Where(f => !File.Exists(Path.Combine(_repoRoot, f)))
                .ToList();

            if (spec.Status is SpecStatus.Draft or SpecStatus.Approved or SpecStatus.InImplementation
                && missing.Count == 0)
            {
                items.Add(new SpecDriftItemDto(
                    spec.Id, spec.Status.ToString(), nameof(SpecStatus.Done),
                    "Todos os arquivos citados na spec já existem no repositório.",
                    []));
            }
            else if (spec.Status == SpecStatus.Done && missing.Count > 0)
            {
                items.Add(new SpecDriftItemDto(
                    spec.Id, spec.Status.ToString(), nameof(SpecStatus.Deprecated),
                    "Spec marcada como Done referencia arquivos que não existem mais.",
                    missing));
            }
        }

        return Task.FromResult(new SpecDriftReportDto(total, items.Count, items));
    }

    private static string? ResolveSpecsDir(string? configured)
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
}
