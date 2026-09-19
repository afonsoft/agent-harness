using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Spectre.Console.Cli;
using Taskboard.Cli.Services;
using Taskboard.Requests;

namespace Taskboard.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<AppRootCommand>();
        app.Configure(config =>
        {
            config.AddCommand<ContextCurrentCommand>("context:current");




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
