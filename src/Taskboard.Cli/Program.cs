using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Spectre.Console.Cli;
using Taskboard.Cli.Services;
using Taskboard.Requests;
using Taskboard.ValueObjects;

namespace Taskboard.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<AppRootCommand>();
        app.Configure(config =>
        {
            config.AddCommand<ContextCurrentCommand>("context:current");
            config.AddCommand<ProjectListCommand>("project:list");
            config.AddCommand<ProjectCreateCommand>("project:create");
            config.AddCommand<ProjectMapCommand>("project:map");

            config.AddCommand<IssueListCommand>("issue:list");
            config.AddCommand<IssueGetCommand>("issue:get");
            config.AddCommand<IssueCreateCommand>("issue:create");
            config.AddCommand<IssueUpdateCommand>("issue:update");
            config.AddCommand<IssueMoveCommand>("issue:move");
            config.AddCommand<IssueArchiveCommand>("issue:archive");
            config.AddCommand<IssueRestoreCommand>("issue:restore");
            config.AddCommand<IssueRelationCommand>("issue:relation");

            config.AddCommand<CommentListCommand>("comment:list");
            config.AddCommand<CommentAddCommand>("comment:add");
            config.AddCommand<CommentUpdateCommand>("comment:update");
            config.AddCommand<CommentDeleteCommand>("comment:delete");

            config.AddCommand<AttachmentUploadCommand>("attachment:upload");
            config.AddCommand<AttachmentDownloadCommand>("attachment:download");

            // GitHub board: history + comments (SPEC-20260918-github-comments-history)
            config.AddCommand<GitHubIssueHistoryCommand>("ghissue:history");
            config.AddCommand<GitHubIssueCommentListCommand>("ghissue:comments");
            config.AddCommand<GitHubIssueCommentAddCommand>("ghissue:comment");

            config.AddCommand<CloudLoginCommand>("cloud:login");
            config.AddCommand<CloudStatusCommand>("cloud:status");
            config.AddCommand<CloudLogoutCommand>("cloud:logout");
        });

        try
        {
            return await app.RunAsync(args);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return 1;
        }
    }

    internal static string ResolveBaseUrl(string? urlArg)
    {
        var env = Environment.GetEnvironmentVariable("TASKBOARD_URL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        if (!string.IsNullOrWhiteSpace(urlArg))
        {
            return urlArg;
        }

        return CliConfigService.Load().BaseUrl;
    }

    internal static async Task<int> RunAsync(GlobalSettings settings, Func<TaskboardApiClient, CancellationToken, Task<int>> action, CancellationToken cancellationToken = default)
    {
        var client = new TaskboardApiClient(ResolveBaseUrl(settings.Url));
        try
        {
            return await action(client, cancellationToken);
        }
        catch (CliException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }

    internal static async Task<int> WriteOutputAsync(bool json, JsonNode? node, string? extractPath = null)
    {
        if (extractPath is not null && node is not null)
        {
            node = node[extractPath];
        }

        if (json)
        {
            Console.WriteLine(node?.ToJsonString() ?? "{}");
            return 0;
        }

        if (node is null)
        {
            Console.WriteLine("OK");
            return 0;
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                Console.WriteLine(item?.ToJsonString() ?? string.Empty);
            }
        }
        else if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("id", out var id) && id is not null)
            {
                Console.WriteLine($"{id}: {obj["title"] ?? obj["name"] ?? obj}");
            }
            else
            {
                Console.WriteLine(obj.ToJsonString());
            }
        }
        else
        {
            Console.WriteLine(node.ToJsonString());
        }

        return 0;
    }

    internal static async Task<string?> ResolveTaskIdAsync(TaskboardApiClient client, string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var all = await client.GetAsync("/api/tasks", ct);
        var tasks = all?["tasks"] as JsonArray;
        if (tasks is null)
        {
            return null;
        }

        foreach (var task in tasks)
        {
            if (task?["identifier"]?.GetValue<string>() == identifier || task?["id"]?.GetValue<string>() == identifier)
            {
                return task["id"]?.GetValue<string>();
            }
        }

        return null;
    }

    internal static async Task<(string? TaskId, string? CommentId)> ResolveCommentAsync(TaskboardApiClient client, string commentId, CancellationToken ct)
    {
        var all = await client.GetAsync("/api/tasks", ct);
        var tasks = all?["tasks"] as JsonArray;
        if (tasks is null)
        {
            return (null, null);
        }

        foreach (var task in tasks)
        {
            var id = task?["id"]?.GetValue<string>();
            if (id is null)
            {
                continue;
            }

            var comments = await client.GetAsync($"/api/tasks/{id}/comments", ct);
            var list = comments?["comments"] as JsonArray;
            if (list is null)
            {
                continue;
            }

            foreach (var c in list)
            {
                if (c?["id"]?.GetValue<string>() == commentId)
                {
                    return (id, commentId);
                }
            }
        }

        return (null, null);
    }

    internal static void AddIfSet(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}

public class EmptySettings : CommandSettings
{
}

public class GlobalSettings : CommandSettings
{
    [CommandOption("--url")]
    public string? Url { get; set; }

    [CommandOption("--json")]
    public bool Json { get; set; }
}

public class AppRootCommand : AsyncCommand<EmptySettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine("taskctl - [green]Taskboard CLI[/]");
        Console.WriteLine("Use [blue]taskctl --help[/] para listar comandos.");
        return 0;
    }
}

public class ProjectListSettings : GlobalSettings
{
}

public class ProjectListCommand : AsyncCommand<ProjectListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ProjectListSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var result = await client.GetAsync("/api/projects", ct);
            return await Program.WriteOutputAsync(settings.Json, result, "projects");
        }, cancellationToken);
}

public class ProjectCreateSettings : GlobalSettings
{
    [CommandOption("--id")]
    public string? Id { get; set; }

    [CommandOption("--name")]
    public string? Name { get; set; }

    [CommandOption("--workspace-path")]
    public string? WorkspacePath { get; set; }
}

public class ProjectCreateCommand : AsyncCommand<ProjectCreateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ProjectCreateSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            throw new CliException(2, "Informe --name.");
        }

        return await Program.RunAsync(settings, async (client, ct) =>
        {
            var payload = new CreateProjectRequest(settings.Id, settings.Name!, settings.WorkspacePath);
            var result = await client.PostAsync("/api/projects", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "project");
        }, cancellationToken);
    }
}

public class ProjectMapSettings : GlobalSettings
{
    [CommandArgument(0, "<project>")]
    public string Project { get; set; } = default!;

    [CommandOption("--workspace-path")]
    public string? WorkspacePath { get; set; }
}

public class ProjectMapCommand : AsyncCommand<ProjectMapSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ProjectMapSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var config = CliConfigService.Load();
            config.CurrentProject = settings.Project;
            config.CurrentWorkspace = settings.WorkspacePath ?? config.CurrentWorkspace;
            CliConfigService.Save(config);

            var node = new JsonObject
            {
                ["projectId"] = settings.Project,
                ["workspacePath"] = settings.WorkspacePath,
            };
            return await Program.WriteOutputAsync(settings.Json, node);
        }, cancellationToken);
}

public class IssueListSettings : GlobalSettings
{
    [CommandOption("--project")]
    public string? Project { get; set; }

    [CommandOption("--status")]
    public string? Status { get; set; }

    [CommandOption("--archived")]
    public bool? Archived { get; set; }

    [CommandOption("--assignee")]
    public string? Assignee { get; set; }

    [CommandOption("--label")]
    public string? Label { get; set; }
}

public class IssueListCommand : AsyncCommand<IssueListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueListSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var query = new List<string>();
            Program.AddIfSet(query, "projectId", settings.Project);
            Program.AddIfSet(query, "status", settings.Status);
            if (settings.Archived is { } a)
            {
                query.Add($"archived={a.ToString().ToLowerInvariant()}");
            }

            Program.AddIfSet(query, "assigneeId", settings.Assignee);
            Program.AddIfSet(query, "label", settings.Label);

            var path = "/api/tasks" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
            var result = await client.GetAsync(path, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "tasks");
        }, cancellationToken);
}

public class IssueGetSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;
}

public class IssueGetCommand : AsyncCommand<IssueGetSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueGetSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var result = await client.GetAsync($"/api/tasks/{id}", ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueCreateSettings : GlobalSettings
{
    [CommandOption("--project")]
    public string? Project { get; set; }

    [CommandOption("--title")]
    public string? Title { get; set; }

    [CommandOption("--description")]
    public string? Description { get; set; }

    [CommandOption("--status")]
    public string? Status { get; set; }

    [CommandOption("--priority")]
    public string? Priority { get; set; }

    [CommandOption("--sort-order")]
    public double? SortOrder { get; set; }

    [CommandOption("--start-date")]
    public DateTime? StartDate { get; set; }

    [CommandOption("--due-date")]
    public DateTime? DueDate { get; set; }
}

public class IssueCreateCommand : AsyncCommand<IssueCreateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueCreateSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var config = CliConfigService.Load();
            var projectId = settings.Project ?? config.CurrentProject;
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new CliException(2, "Informe --project ou use 'taskctl project map'.");
            }

            if (string.IsNullOrWhiteSpace(settings.Title))
            {
                throw new CliException(2, "Informe --title.");
            }

            var payload = new CreateTaskRequest(
                projectId!,
                settings.Title!,
                settings.Description,
                settings.Status,
                settings.Priority,
                null,
                null,
                settings.SortOrder,
                settings.StartDate,
                settings.DueDate);

            var result = await client.PostAsync("/api/tasks", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueUpdateSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;

    [CommandOption("--title")]
    public string? Title { get; set; }

    [CommandOption("--description")]
    public string? Description { get; set; }

    [CommandOption("--status")]
    public string? Status { get; set; }

    [CommandOption("--priority")]
    public string? Priority { get; set; }

    [CommandOption("--version")]
    public long? Version { get; set; }
}

public class IssueUpdateCommand : AsyncCommand<IssueUpdateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueUpdateSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var current = await client.GetAsync($"/api/tasks/{id}", ct);
            var version = settings.Version ?? current?["task"]?["version"]?.GetValue<long>() ?? 0L;

            var patch = new TaskPatch(
                Title: settings.Title,
                Description: settings.Description,
                Status: settings.Status,
                Priority: settings.Priority);

            var payload = new UpdateTaskRequest(version, patch);
            var result = await client.PatchAsync($"/api/tasks/{id}", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueMoveSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;

    [CommandOption("--status")]
    public string? Status { get; set; }

    [CommandOption("--sort-order")]
    public double? SortOrder { get; set; }
}

public class IssueMoveCommand : AsyncCommand<IssueMoveSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueMoveSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            if (string.IsNullOrWhiteSpace(settings.Status))
            {
                throw new CliException(2, "Informe --status.");
            }

            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var payload = new MoveTaskRequest(settings.Status!, settings.SortOrder);
            var result = await client.PostAsync($"/api/tasks/{id}/move", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueArchiveSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;
}

public class IssueArchiveCommand : AsyncCommand<IssueArchiveSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueArchiveSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var result = await client.PostAsync($"/api/tasks/{id}/archive", null, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueRestoreSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;
}

public class IssueRestoreCommand : AsyncCommand<IssueRestoreSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueRestoreSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var result = await client.PostAsync($"/api/tasks/{id}/restore", null, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "task");
        }, cancellationToken);
}

public class IssueRelationSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;

    [CommandArgument(1, "<action>")]
    public string Action { get; set; } = default!;

    [CommandArgument(2, "<type>")]
    public string Type { get; set; } = default!;

    [CommandArgument(3, "<target>")]
    public string Target { get; set; } = default!;
}

public class IssueRelationCommand : AsyncCommand<IssueRelationSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, IssueRelationSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var sourceId = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (sourceId is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var targetId = await Program.ResolveTaskIdAsync(client, settings.Target, ct);
            if (targetId is null)
            {
                throw new CliException(2, $"Issue '{settings.Target}' não encontrada.");
            }

            if (settings.Action.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new CreateRelationRequest(targetId, settings.Type);
                var result = await client.PostAsync($"/api/tasks/{sourceId}/relations", payload, ct);
                return await Program.WriteOutputAsync(settings.Json, result, "relation");
            }

            if (settings.Action.Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                await client.DeleteAsync($"/api/tasks/{sourceId}/relations/{settings.Type}/{targetId}", ct);
                return await Program.WriteOutputAsync(settings.Json, null);
            }

            throw new CliException(2, "Ação deve ser 'add' ou 'remove'.");
        }, cancellationToken);
}

public class CommentListSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;
}

public class CommentListCommand : AsyncCommand<CommentListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CommentListSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var result = await client.GetAsync($"/api/tasks/{id}/comments", ct);
            return await Program.WriteOutputAsync(settings.Json, result, "comments");
        }, cancellationToken);
}

public class CommentAddSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;

    [CommandArgument(1, "<body>")]
    public string Body { get; set; } = default!;
}

public class CommentAddCommand : AsyncCommand<CommentAddSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CommentAddSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            var payload = new CreateCommentRequest(settings.Body);
            var result = await client.PostAsync($"/api/tasks/{id}/comments", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "comment");
        }, cancellationToken);
}

public class CommentUpdateSettings : GlobalSettings
{
    [CommandArgument(0, "<commentId>")]
    public string CommentId { get; set; } = default!;

    [CommandArgument(1, "<body>")]
    public string Body { get; set; } = default!;
}

public class CommentUpdateCommand : AsyncCommand<CommentUpdateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CommentUpdateSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var (taskId, resolvedCommentId) = await Program.ResolveCommentAsync(client, settings.CommentId, ct);
            if (taskId is null || resolvedCommentId is null)
            {
                throw new CliException(2, $"Comentário '{settings.CommentId}' não encontrado.");
            }

            var payload = new UpdateCommentRequest(settings.Body);
            var result = await client.PatchAsync($"/api/tasks/{taskId}/comments/{resolvedCommentId}", payload, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "comment");
        }, cancellationToken);
}

public class CommentDeleteSettings : GlobalSettings
{
    [CommandArgument(0, "<commentId>")]
    public string CommentId { get; set; } = default!;
}

public class CommentDeleteCommand : AsyncCommand<CommentDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CommentDeleteSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var (taskId, resolvedCommentId) = await Program.ResolveCommentAsync(client, settings.CommentId, ct);
            if (taskId is null || resolvedCommentId is null)
            {
                throw new CliException(2, $"Comentário '{settings.CommentId}' não encontrado.");
            }

            await client.DeleteAsync($"/api/tasks/{taskId}/comments/{resolvedCommentId}", ct);
            return await Program.WriteOutputAsync(settings.Json, null);
        }, cancellationToken);
}

public class AttachmentUploadSettings : GlobalSettings
{
    [CommandArgument(0, "<identifier>")]
    public string Identifier { get; set; } = default!;

    [CommandArgument(1, "<file>")]
    public string File { get; set; } = default!;

    [CommandOption("--kind")]
    public string Kind { get; set; } = "file";
}

public class AttachmentUploadCommand : AsyncCommand<AttachmentUploadSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AttachmentUploadSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            if (!File.Exists(settings.File))
            {
                throw new CliException(2, $"Arquivo '{settings.File}' não encontrado.");
            }

            var id = await Program.ResolveTaskIdAsync(client, settings.Identifier, ct);
            if (id is null)
            {
                throw new CliException(2, $"Issue '{settings.Identifier}' não encontrada.");
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(id), "taskId");
            content.Add(new StringContent(settings.Kind), "kind");
            using var stream = File.OpenRead(settings.File);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", Path.GetFileName(settings.File));

            var result = await client.PostMultipartAsync("/api/attachments", content, ct);
            return await Program.WriteOutputAsync(settings.Json, result, "attachment");
        }, cancellationToken);
}

public class AttachmentDownloadSettings : GlobalSettings
{
    [CommandArgument(0, "<attachmentId>")]
    public string AttachmentId { get; set; } = default!;

    [CommandArgument(1, "<output>")]
    public string Output { get; set; } = default!;
}

public class AttachmentDownloadCommand : AsyncCommand<AttachmentDownloadSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AttachmentDownloadSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            using var stream = File.Create(settings.Output);
            await client.DownloadAsync($"/api/attachments/{settings.AttachmentId}/download", stream, ct);
            var node = new JsonObject { ["downloaded"] = settings.Output };
            return await Program.WriteOutputAsync(settings.Json, node);
        }, cancellationToken);
}

public class CloudLoginSettings : GlobalSettings
{
    [CommandArgument(0, "<url>")]
    public string? CloudUrlArg { get; set; }
}

public class CloudLoginCommand : AsyncCommand<CloudLoginSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CloudLoginSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var config = CliConfigService.Load();
            if (!string.IsNullOrWhiteSpace(settings.CloudUrlArg))
            {
                config.CloudUrl = settings.CloudUrlArg;
                CliConfigService.Save(config);
            }

            await client.PutAsync("/api/local/cloud-session", new { connected = true }, ct);
            var node = new JsonObject
            {
                ["connected"] = true,
                ["cloudUrl"] = config.CloudUrl,
            };
            return await Program.WriteOutputAsync(settings.Json, node);
        }, cancellationToken);
}

public class CloudStatusSettings : GlobalSettings
{
}

public class CloudStatusCommand : AsyncCommand<CloudStatusSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CloudStatusSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var result = await client.GetAsync("/api/local/cloud-session", ct);
            return await Program.WriteOutputAsync(settings.Json, result);
        }, cancellationToken);
}

public class CloudLogoutSettings : GlobalSettings
{
}

public class CloudLogoutCommand : AsyncCommand<CloudLogoutSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CloudLogoutSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var config = CliConfigService.Load();
            await client.PutAsync("/api/local/cloud-session", new { connected = false }, ct);
            config.CloudUrl = null;
            CliConfigService.Save(config);
            var node = new JsonObject { ["connected"] = false };
            return await Program.WriteOutputAsync(settings.Json, node);
        }, cancellationToken);
}

public class ContextCurrentSettings : GlobalSettings
{
}

public class ContextCurrentCommand : AsyncCommand<ContextCurrentSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ContextCurrentSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var config = CliConfigService.Load();
            var url = Program.ResolveBaseUrl(settings.Url);
            var node = new JsonObject
            {
                ["baseUrl"] = url,
                ["currentProject"] = config.CurrentProject,
                ["currentWorkspace"] = config.CurrentWorkspace,
                ["cloudUrl"] = config.CloudUrl,
            };
            return await Program.WriteOutputAsync(settings.Json, node);
        }, cancellationToken);
}

// ---- GitHub board: issue history + comments (SPEC-20260918-github-comments-history RF-003) ----
// These commands talk to the GitHub board surface (/api/github/...), identified
// by owner/repo + issue number — distinct from local tasks (TASK-<project>-<n>).

public class GitHubIssueHistorySettings : GlobalSettings
{
    [CommandArgument(0, "<issueId>")]
    public string IssueId { get; set; } = default!;

    [CommandOption("--take")]
    public int? Take { get; set; }
}

public class GitHubIssueHistoryCommand : AsyncCommand<GitHubIssueHistorySettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueHistorySettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var query = settings.Take is { } t ? $"?take={t}" : string.Empty;
            var result = await client.GetAsync(
                $"/api/github/issues/{Uri.EscapeDataString(settings.IssueId)}/history{query}", ct);
            return await Program.WriteOutputAsync(settings.Json, result, "items");
        }, cancellationToken);
}

public class GitHubIssueCommentListSettings : GlobalSettings
{
    [CommandArgument(0, "<repo>")]
    public string Repo { get; set; } = default!;

    [CommandArgument(1, "<number>")]
    public int Number { get; set; }

    [CommandOption("--take")]
    public int? Take { get; set; }
}

public class GitHubIssueCommentListCommand : AsyncCommand<GitHubIssueCommentListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueCommentListSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var (owner, name) = SplitRepo(settings.Repo);
            var query = settings.Take is { } t ? $"?take={t}" : string.Empty;
            var result = await client.GetAsync(
                $"/api/github/repos/{owner}/{name}/issues/{settings.Number}/comments{query}", ct);
            return await Program.WriteOutputAsync(settings.Json, result, "comments");
        }, cancellationToken);

    internal static (string Owner, string Name) SplitRepo(string repo)
    {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new CliException(2, "Repositório deve ser 'owner/name'.");
        }

        return (Uri.EscapeDataString(parts[0]), Uri.EscapeDataString(parts[1]));
    }
}

public class GitHubIssueCommentAddSettings : GlobalSettings
{
    [CommandArgument(0, "<repo>")]
    public string Repo { get; set; } = default!;

    [CommandArgument(1, "<number>")]
    public int Number { get; set; }

    [CommandArgument(2, "<body>")]
    public string Body { get; set; } = default!;
}

public class GitHubIssueCommentAddCommand : AsyncCommand<GitHubIssueCommentAddSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueCommentAddSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            if (string.IsNullOrWhiteSpace(settings.Body))
            {
                throw new CliException(2, "O corpo do comentário é obrigatório.");
            }

            var (owner, name) = GitHubIssueCommentListCommand.SplitRepo(settings.Repo);
            var result = await client.PostAsync(
                $"/api/github/repos/{owner}/{name}/issues/{settings.Number}/comments",
                new AddIssueCommentRequest(settings.Body), ct);
            return await Program.WriteOutputAsync(settings.Json, result, "comment");
        }, cancellationToken);
}
