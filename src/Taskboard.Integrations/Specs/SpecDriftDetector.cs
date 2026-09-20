using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Dtos;
using Taskboard.Specs;

namespace Taskboard.Integrations.Specs;

/// <summary>
/// File-existence drift detector (SPEC-20260919-ade-living-specs RF-002):
/// a spec in Draft/Approved/InImplementation whose "Files to create or
/// modify" all exist on disk is stale — suggest Done. A Done spec whose
/// referenced files were since deleted drifts the other way — suggest
/// Deprecated. Repo root is the parent of the resolved <c>.specs</c> dir.
/// SPEC-20260920 RF-005: a <c>repo</c> param resolves
/// <c>~/repos/&lt;name&gt;/.specs</c> on demand.
/// </summary>
public sealed class SpecDriftDetector : ISpecDriftDetector
{
    private readonly ISpecDocumentParser _parser;
    private readonly IWorkspacePathResolver _workspace;
    private readonly string? _defaultSpecsDir;
    private readonly string? _defaultRepoRoot;

    public SpecDriftDetector(
        ISpecDocumentParser parser,
        IConfiguration configuration,
        IWorkspacePathResolver workspace)
    {
        _parser = parser;
        _workspace = workspace;
        _defaultSpecsDir = ResolveSpecsDir(configuration["Taskboard:SpecsDir"]);
        _defaultRepoRoot = _defaultSpecsDir is null ? null : Directory.GetParent(_defaultSpecsDir)?.FullName;
    }

    public Task<SpecDriftReportDto> BuildReportAsync(
        string? repo = null, CancellationToken cancellationToken = default)
    {
        var (specsDir, repoRoot) = ResolveDirs(repo);
        if (specsDir is null || repoRoot is null || !Directory.Exists(specsDir))
        {
            return Task.FromResult(new SpecDriftReportDto(0, 0, []));
        }

        var items = new List<SpecDriftItemDto>();
        var total = 0;
        foreach (var file in Directory.EnumerateFiles(specsDir, "SPEC-*.md"))
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
                .Where(f => !File.Exists(Path.Combine(repoRoot, f)))
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

    /// <summary>
    /// SPEC-20260920 RF-005: <paramref name="repo"/> → the clone's
    /// <c>.specs</c> + clone root; absent → the configured default dirs.
    /// </summary>
    private (string? SpecsDir, string? RepoRoot) ResolveDirs(string? repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return (_defaultSpecsDir, _defaultRepoRoot);
        }
        var workdir = _workspace.ResolveCardWorkdir(repo, out var cloneExists);
        if (!cloneExists)
        {
            return (null, null);
        }
        var specsDir = Path.Combine(workdir, ".specs");
        return Directory.Exists(specsDir) ? (specsDir, workdir) : (null, null);
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
