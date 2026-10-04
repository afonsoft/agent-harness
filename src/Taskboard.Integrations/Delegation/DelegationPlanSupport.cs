using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Delegation;

/// <summary>One spec of a delegation plan (tool arg or coordinator-emitted JSON).</summary>
public sealed record DelegationPlanSpec(
    string Prompt, string? Cli, IReadOnlyList<int> Deps, bool UseWorktree);

/// <summary>One created child task of a materialized plan.</summary>
public sealed record DelegationPlanItem(int Index, string Id, string Cli);

/// <summary>
/// SPEC-20261009 RF-001: plan spec parsing shared by the delegate_plan tool and
/// the coordinator task runner. Deps are indices into the same tasks array and
/// must point at earlier entries so the DAG never cycles.
/// </summary>
public static class DelegationPlanParser
{
    public static List<DelegationPlanSpec>? ParseTasks(
        JsonElement tasksEl, int maxTasks, out string? error)
    {
        error = null;
        var specs = new List<DelegationPlanSpec>();
        var index = 0;
        foreach (var el in tasksEl.EnumerateArray())
        {
            var prompt = ReadString(el, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                error = $"tasks[{index}].prompt is required";
                return null;
            }

            var deps = ReadDeps(el);
            if (deps is null)
            {
                error = $"tasks[{index}].deps must be integers";
                return null;
            }

            specs.Add(new DelegationPlanSpec(
                prompt,
                ReadString(el, "cli"),
                deps,
                el.TryGetProperty("use_worktree", out var uw) && uw.ValueKind == JsonValueKind.True));
            index++;
        }

        if (specs.Count == 0)
        {
            error = "tasks array is empty";
            return null;
        }

        if (specs.Count > maxTasks)
        {
            error = $"plan supports at most {maxTasks} tasks";
            return null;
        }

        for (var i = 0; i < specs.Count; i++)
        {
            var bad = specs[i].Deps.FirstOrDefault(dep => dep >= i || dep < 0, int.MinValue);
            if (bad != int.MinValue)
            {
                error = $"tasks[{i}].deps must point to earlier tasks (got {bad})";
                return null;
            }
        }

        return specs;
    }

    public static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static IReadOnlyList<int>? ReadDeps(JsonElement el)
    {
        if (!el.TryGetProperty("deps", out var p) || p.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var deps = new List<int>();
        foreach (var dep in p.EnumerateArray())
        {
            if (dep.ValueKind != JsonValueKind.Number || !dep.TryGetInt32(out var value))
            {
                return null;
            }

            deps.Add(value);
        }

        return deps;
    }
}

/// <summary>
/// SPEC-20261009 RF-001: extracts the plan a coordinator CLI emitted — the last
/// balanced-brace JSON object in its output carrying a "tasks" array. Tolerant
/// to prose around the JSON and markdown fences.
/// </summary>
public static class CoordinatorPlanExtractor
{
    public static bool TryExtract(string output, out JsonElement tasks)
    {
        tasks = default;
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        // Scan every '{' position; try to close a balanced object (braces inside
        // strings don't count) and parse it. Keep the last one with "tasks".
        var found = false;
        for (var start = 0; start < output.Length; start++)
        {
            if (output[start] != '{')
            {
                continue;
            }

            var end = FindObjectEnd(output, start);
            if (end < 0)
            {
                continue;
            }

            if (TryParseTasksObject(output, start, end, ref tasks))
            {
                found = true;
            }
        }

        return found;
    }

    private static bool TryParseTasksObject(
        string text, int start, int end, ref JsonElement tasks)
    {
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("tasks", out var t)
                && t.ValueKind == JsonValueKind.Array)
            {
                tasks = t.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
            // Not a JSON object — keep scanning.
        }

        return false;
    }

    /// <summary>End index (inclusive) of the balanced-brace object at <paramref name="start"/>, or -1.</summary>
    internal static int FindObjectEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }
}

/// <summary>
/// SPEC-20261009 RF-001: materializes a validated plan into delegation tasks —
/// creates them in order so dep indices resolve to real ids, rolls everything
/// back when a mid-plan create fails so a broken plan never leaves a half-DAG.
/// Shared by the delegate_plan tool and the coordinator task runner.
/// </summary>
public sealed class DelegationPlanCreator(IServiceScopeFactory scopeFactory)
{
    public const int MaxTasks = 10;

    public async Task<IReadOnlyList<DelegationPlanItem>> CreateAsync(
        IReadOnlyList<DelegationPlanSpec> specs,
        string? repoPath,
        string baseBranch,
        string scope,
        string workspacePath,
        string? defaultCli,
        CancellationToken ct)
    {
        var created = new List<DelegationPlanItem>();
        var createdIds = new List<string>();
        IDelegationService? service = null;

        try
        {
            await using var diScope = scopeFactory.CreateAsyncScope();
            service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
            var isolation = diScope.ServiceProvider.GetService<IWorkspaceIsolationService>();

            for (var i = 0; i < specs.Count; i++)
            {
                var task = await CreateOneAsync(
                    specs[i], createdIds, service, isolation, repoPath, baseBranch,
                    scope, workspacePath, defaultCli, ct).ConfigureAwait(false);
                createdIds.Add(task.Id);
                created.Add(new DelegationPlanItem(i, task.Id, task.CliName));
            }
        }
        catch (Exception)
        {
            await RollbackAsync(service, createdIds).ConfigureAwait(false);
            throw;
        }

        return created;
    }

    private static async Task<DelegationTaskDto> CreateOneAsync(
        DelegationPlanSpec spec, List<string> createdIds, IDelegationService service,
        IWorkspaceIsolationService? isolation, string? repoPath, string baseBranch,
        string scope, string workspacePath, string? defaultCli, CancellationToken ct)
    {
        var dependsOn = spec.Deps.Count == 0
            ? null
            : spec.Deps.Select(d => createdIds[d]).ToList();
        var cli = string.IsNullOrWhiteSpace(spec.Cli)
            ? defaultCli ?? "opencode"
            : spec.Cli!.Trim();

        var task = await service.CreateTaskAsync(
            new CreateDelegationTaskRequest(
                spec.Prompt, cli, scope, workspacePath,
                dependsOn, RetryOf: null, FanoutGroupId: null,
                spec.UseWorktree, repoPath, BaseCommitSha: null),
            ct).ConfigureAwait(false);

        if (!spec.UseWorktree)
        {
            return task;
        }

        if (isolation is null)
        {
            throw new InvalidOperationException("worktree service unavailable");
        }

        var session = await isolation.CreateWorktreeAsync(
            task.Id, repoPath!, baseBranch,
            $"plan-{task.Id[^Math.Min(8, task.Id.Length)..]}",
            retainOnFailure: false, ct).ConfigureAwait(false);
        return (await service.AttachWorktreeAsync(task.Id, session.RunId, ct)
            .ConfigureAwait(false)) ?? task;
    }

    private static async Task RollbackAsync(IDelegationService? service, List<string> createdIds)
    {
        if (service is null)
        {
            return;
        }

        foreach (var id in createdIds)
        {
            try
            {
                await service.CancelTaskAsync(id, "plan-aborted", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (DomainException)
            {
                // Best-effort rollback — a task that already started keeps running.
            }
        }
    }
}
