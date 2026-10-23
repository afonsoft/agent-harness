using Taskboard.Domain.Shared.Configuration;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Spectre.Console.Cli;
using Taskboard.Agents;
using Taskboard.Cli.Services;
using Taskboard.Dtos;
using Taskboard.Requests;

namespace Taskboard.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<AppRootCommand>();
        app.Configure(ConfigureCommands);

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

    internal static void ConfigureCommands(IConfigurator config)
    {
        config.AddCommand<ContextCurrentCommand>("context:current");

        // GitHub board: history + comments (SPEC-20260918-github-comments-history)
        config.AddCommand<GitHubIssueHistoryCommand>("ghissue:history");
        config.AddCommand<GitHubIssueCommentListCommand>("ghissue:comments");
        config.AddCommand<GitHubIssueCommentAddCommand>("ghissue:comment");

        config.AddCommand<CloudLoginCommand>("cloud:login");
        config.AddCommand<CloudStatusCommand>("cloud:status");
        config.AddCommand<CloudLogoutCommand>("cloud:logout");

        // Local SQLite backup/restore (SPEC-20261003-sqlite-backup)
        config.AddCommand<BackupCommand>("backup");
        config.AddCommand<RestoreCommand>("restore");

        // Run preflight doctor + dry-run dispatch (SPEC-20261023-p0-agent-hardening RF-C02/C03)
        config.AddCommand<DoctorCommand>("doctor");
        config.AddCommand<RunCommand>("run");
    }

    internal static string ResolveBaseUrl(string? urlArg)
    {
        var env = HarnessEnv.Get("HARNESS_URL");
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

    internal static async Task<int> RunAsync(GlobalSettings settings, Func<CliTaskboardApiClient, CancellationToken, Task<int>> action, CancellationToken cancellationToken = default)
    {
        var client = new CliTaskboardApiClient(ResolveBaseUrl(settings.Url));
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

    /// <summary>
    /// SPEC-20261023 RF-C01/C02: renders a preflight report
    /// (<c>{ verdict, checks[] }</c>) — raw JSON when <paramref name="json"/>,
    /// otherwise a status/name/message table plus next-action hints and the
    /// verdict line. Returns the doctor exit code: 0 on ready/warning, 2 on
    /// blocked or unparseable output.
    /// </summary>
    internal static async Task<int> WritePreflightReportAsync(JsonNode? node, bool json)
    {
        if (node is not JsonObject report)
        {
            await Console.Error.WriteLineAsync("error: resposta de preflight inesperada.");
            return 2;
        }

        var verdict = report["verdict"]?.GetValue<string>() ?? "blocked";
        if (json)
        {
            Console.WriteLine(report.ToJsonString());
            return ExitCodeForVerdict(verdict);
        }

        var checks = report["checks"] as JsonArray ?? [];
        var nameWidth = checks
            .Select(c => (c?["name"]?.GetValue<string>() ?? string.Empty).Length)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var item in checks)
        {
            if (item is not JsonObject check)
            {
                continue;
            }

            var status = PreflightStatusText(check["status"]);
            var name = check["name"]?.GetValue<string>() ?? string.Empty;
            var message = check["message"]?.GetValue<string>() ?? string.Empty;
            Console.WriteLine($"{status,-8}{name.PadRight(nameWidth)}  {message}");
            if (check["nextAction"]?.GetValue<string>() is { Length: > 0 } next)
            {
                Console.WriteLine($"{string.Empty,-8}{new string(' ', nameWidth)}  -> {next}");
            }
        }

        Console.WriteLine($"verdict: {verdict}");
        return ExitCodeForVerdict(verdict);
    }

    internal static int ExitCodeForVerdict(string? verdict) =>
        string.Equals(verdict, "ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(verdict, "warning", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;

    private static string PreflightStatusText(JsonNode? status) => status switch
    {
        JsonValue v when v.TryGetValue<int>(out var n) => n switch
        {
            0 => "ok",
            1 => "warning",
            2 => "blocked",
            _ => n.ToString(),
        },
        JsonValue v when v.TryGetValue<string>(out var s) => s.ToLowerInvariant(),
        _ => "?",
    };
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
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine("taskctl - [green]Taskboard CLI[/]");
        Console.WriteLine("Use [blue]taskctl --help[/] para listar comandos.");
        return 0;
    }
}

public class CloudLoginSettings : GlobalSettings
{
    // Convention: CommandArgument names must be "<name>" or "[name]" — a bare
    // name is parsed as markup and crashes every --help render (gotchas.md).
    [CommandArgument(0, "<url>")]
    public string? CloudUrlArg { get; set; }
}

public class CloudLoginCommand : AsyncCommand<CloudLoginSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CloudLoginSettings settings, CancellationToken cancellationToken)
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
    public override string ToString() => "cloud status";
}

public class CloudStatusCommand : AsyncCommand<CloudStatusSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CloudStatusSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var result = await client.GetAsync("/api/local/cloud-session", ct);
            return await Program.WriteOutputAsync(settings.Json, result);
        }, cancellationToken);
}

public class CloudLogoutSettings : GlobalSettings
{
    public override string ToString() => "cloud logout";
}

public class CloudLogoutCommand : AsyncCommand<CloudLogoutSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CloudLogoutSettings settings, CancellationToken cancellationToken)
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
    public override string ToString() => "context current";
}

public class ContextCurrentCommand : AsyncCommand<ContextCurrentSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContextCurrentSettings settings, CancellationToken cancellationToken)
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
    public override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueHistorySettings settings, CancellationToken cancellationToken)
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
    public override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueCommentListSettings settings, CancellationToken cancellationToken)
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
    public override async Task<int> ExecuteAsync(CommandContext context, GitHubIssueCommentAddSettings settings, CancellationToken cancellationToken)
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

// ---- Local SQLite backup/restore (SPEC-20261003-sqlite-backup) ----
// These commands touch files directly — no server round-trip. The DB path
// resolves via --db > HARNESS_DATA_DIR > ~/.agent-harness.

public class BackupSettings : GlobalSettings
{
    [CommandArgument(0, "<destDir>")]
    public string DestDir { get; set; } = default!;

    [CommandOption("--keep")]
    public int? Keep { get; set; }

    [CommandOption("--db")]
    public string? Db { get; set; }
}

public class BackupCommand : AsyncCommand<BackupSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BackupSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var source = DatabaseBackupService.ResolveDatabasePath(settings.Db);
            var dest = await Task.Run(
                () => DatabaseBackupService.Backup(source, settings.DestDir, settings.Keep ?? 7),
                cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(dest);
            return 0;
        }
        catch (CliException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }
}

public class RestoreSettings : GlobalSettings
{
    [CommandArgument(0, "<backupFile>")]
    public string BackupFile { get; set; } = default!;

    [CommandOption("--db")]
    public string? Db { get; set; }

    [CommandOption("--force")]
    public bool Force { get; set; }
}

public class RestoreCommand : AsyncCommand<RestoreSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RestoreSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var target = DatabaseBackupService.ResolveDatabasePath(settings.Db);
            await Task.Run(
                () => DatabaseBackupService.Restore(settings.BackupFile, target, settings.Force),
                cancellationToken).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(target);
            return 0;
        }
        catch (CliException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }
    }
}

// ---- Run preflight doctor + dry-run dispatch (SPEC-20261023-p0-agent-hardening) ----

public class DoctorSettings : GlobalSettings
{
    public override string ToString() => "doctor";
}

public class DoctorCommand : AsyncCommand<DoctorSettings>
{
    /// <summary>RF-C02: preflight report — exit 0 on ready/warning, 2 on blocked.</summary>
    public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            var report = await client.GetAsync("/api/harness/preflight", ct);
            return await Program.WritePreflightReportAsync(report, settings.Json);
        }, cancellationToken);
}

public class RunSettings : GlobalSettings
{
    [CommandArgument(0, "<prompt>")]
    public string Prompt { get; set; } = default!;

    [CommandOption("--template")]
    public string? TemplateId { get; set; }

    [CommandOption("--repo")]
    public string? Repo { get; set; }

    [CommandOption("--branch")]
    public string Branch { get; set; } = "main";

    [CommandOption("--issue")]
    public string? IssueId { get; set; }

    [CommandOption("--spec")]
    public string? SpecPath { get; set; }

    [CommandOption("--max-budget")]
    public decimal? MaxBudgetUsd { get; set; }

    [CommandOption("--agent")]
    public string? Agent { get; set; }

    [CommandOption("--tier")]
    public string? Tier { get; set; }

    [CommandOption("--single-agent")]
    public bool SingleAgent { get; set; }

    [CommandOption("--skip-verification")]
    public bool SkipVerification { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }
}

public class RunCommand : AsyncCommand<RunSettings>
{
    /// <summary>
    /// RF-C03: <c>--dry-run</c> resolves config + preflights server-side —
    /// nothing persists, nothing spawns. Without it, dispatches a real run.
    /// </summary>
    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings settings, CancellationToken cancellationToken)
        => await Program.RunAsync(settings, async (client, ct) =>
        {
            if (string.IsNullOrWhiteSpace(settings.Prompt))
            {
                throw new CliException(2, "O prompt é obrigatório.");
            }

            if (string.IsNullOrWhiteSpace(settings.Repo))
            {
                throw new CliException(2, "--repo é obrigatório (owner/name).");
            }

            var agent = ParseEnum<AgentType>(settings.Agent, "--agent");
            var tier = ParseEnum<AgentModelTier>(settings.Tier, "--tier");
            var singleAgent = settings.SingleAgent || agent is not null || tier is not null;
            var templateId = settings.TemplateId
                ?? (singleAgent ? PipelineTemplateIds.SingleAgent : "quick-patch");
            var isSingleAgentTemplate = templateId == PipelineTemplateIds.SingleAgent;

            var result = await client.PostAsync("/api/harness/runs",
                new RunStartRequest(
                    templateId, settings.Repo, settings.Branch,
                    settings.IssueId, settings.SpecPath, settings.Prompt,
                    settings.MaxBudgetUsd,
                    AgentOverride: isSingleAgentTemplate ? agent : null,
                    TierOverride: isSingleAgentTemplate ? tier : null,
                    settings.SkipVerification,
                    StageOverrides: null, singleAgent, agent, tier,
                    DryRun: settings.DryRun), ct);

            if (settings.DryRun)
            {
                if (!settings.Json && result?["resolved"] is JsonObject resolved)
                {
                    Console.WriteLine("resolved:");
                    foreach (var kv in resolved)
                    {
                        Console.WriteLine($"  {kv.Key}: {kv.Value?.ToJsonString() ?? "null"}");
                    }
                }

                return await Program.WritePreflightReportAsync(result, settings.Json);
            }

            return await Program.WriteOutputAsync(settings.Json, result);
        }, cancellationToken);

    private static TEnum? ParseEnum<TEnum>(string? value, string option)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new CliException(2, $"{option} inválido: '{value}'.");
    }
}
