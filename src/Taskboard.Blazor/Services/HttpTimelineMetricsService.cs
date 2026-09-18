using System.Net.Http.Json;
using Taskboard.GitHub;

namespace Taskboard.Blazor.Services;

/// <summary>
/// <see cref="ITimelineMetricsService"/> implementation backed by the
/// server-side <c>/api/github/repos/{owner}/{repo}/timeline|metrics</c>
/// endpoints (SPEC-20260918-gantt-github-timeline) — all aggregation happens
/// on the server.
/// </summary>
public sealed class HttpTimelineMetricsService(HttpClient http) : ITimelineMetricsService
{
    public async Task<RepoTimelineDto> GetTimelineAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<RepoTimelineDto>(
            $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/timeline?days={days}",
            cancellationToken);
        return result ?? new RepoTimelineDto([], []);
    }

    public async Task<RepoMetricsDto> GetMetricsAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<RepoMetricsDto>(
            $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/metrics?days={days}",
            cancellationToken);
        return result ?? new RepoMetricsDto(null, null, null, [], 0, null);
    }
}
