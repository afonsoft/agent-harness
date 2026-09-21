using Microsoft.JSInterop;
using Taskboard.GitHub;

namespace Taskboard.Blazor.Services;

/// <summary>
/// SPEC-20260920-global-repo-selector RF-001: single repository selection
/// shared by every repo-scoped screen (Board, Gantt, Workflow, Specs,
/// VS Code, Terminal). The value persists in
/// <c>localStorage["harness.selectedRepo"]</c>; when storage is unavailable
/// the selection is session-only. The repo list is fetched once per app
/// session and exposed via <see cref="Repositories"/>.
/// </summary>
public sealed class SelectedRepositoryService
{
    /// <summary>localStorage key holding the selected <c>owner/repo</c>.</summary>
    public const string StorageKey = "harness.selectedRepo";

    private readonly IGitHubService _gitHub;
    private readonly IJSRuntime _js;
    private Task? _loadTask;
    private string? _selected;

    public SelectedRepositoryService(IGitHubService gitHub, IJSRuntime js)
    {
        _gitHub = gitHub;
        _js = js;
    }

    /// <summary>All selectable repositories (<c>owner/repo</c>), sorted.</summary>
    public IReadOnlyList<string> Repositories { get; private set; } = [];

    /// <summary>Current selection — null when nothing valid is available.</summary>
    public string? Selected => _selected;

    /// <summary>GitHub token is not configured — the selector stays disabled.</summary>
    public bool TokenMissing { get; private set; }

    /// <summary>Last load warning (token missing or repo-list fetch failure).</summary>
    public string? Warning { get; private set; }

    /// <summary>True once the initial load finished (success or failure).</summary>
    public bool IsLoaded => _loadTask is { IsCompleted: true };

    /// <summary>Raised after <see cref="Selected"/> effectively changes.</summary>
    public event Func<Task>? Changed;

    /// <summary>Fetches the repo list and resolves the initial selection once.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    /// <summary>
    /// Validates and commits <paramref name="value"/>: persists it and raises
    /// <see cref="Changed"/> only on an effective change. Returns false when
    /// the value is not a well-formed <c>owner/repo</c> — the caller restores
    /// the previous value and shows its own warning.
    /// </summary>
    public async Task<bool> SelectAsync(string? value)
    {
        var normalized = RepositoryFilter.NormalizeCommit(value);
        if (!RepositoryFilter.IsValidRepositoryName(normalized))
        {
            return false;
        }

        if (string.Equals(normalized, _selected, StringComparison.Ordinal))
        {
            return true;
        }

        _selected = normalized;
        await PersistAsync(normalized).ConfigureAwait(false);
        await RaiseChangedAsync().ConfigureAwait(false);
        return true;
    }

    private async Task LoadAsync()
    {
        try
        {
            var repositories = await _gitHub.GetRepositoriesAsync().ConfigureAwait(false);
            Repositories = repositories
                .Select(r => r.FullName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            TokenMissing = true;
            Warning = "GitHub token not configured. Set the GITHUB_TOKEN environment variable or configure it in Settings.";
        }
        catch (Exception ex)
        {
            Warning = $"Could not load GitHub repositories: {ex.Message}. You can still type a repository name.";
        }

        var persisted = await GetPersistedAsync().ConfigureAwait(false);
        _selected = RepositoryFilter.IsValidRepositoryName(persisted)
            ? RepositoryFilter.NormalizeCommit(persisted)
            : _selected ?? Repositories.FirstOrDefault();

        if (_selected is not null)
        {
            await PersistAsync(_selected).ConfigureAwait(false);
        }
    }

    private async Task<string?> GetPersistedAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("taskboard.getSelectedRepo").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null; // storage unavailable — session-only state
        }
    }

    private async Task PersistAsync(string value)
    {
        try
        {
            await _js.InvokeAsync<string?>("taskboard.setSelectedRepo", value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // storage unavailable — selection lives for the session only
        }
    }

    private async Task RaiseChangedAsync()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (Func<Task> handler in handlers.GetInvocationList())
        {
            await handler().ConfigureAwait(false);
        }
    }
}
