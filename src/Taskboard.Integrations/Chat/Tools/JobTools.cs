using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261005-chat-jobs-schedule-search RF-002: inspect background jobs
/// spawned via <c>shell_exec run_in_background</c> — the service is a
/// singleton shared with the jobs endpoints.
/// </summary>
public sealed class JobListTool(IChatJobService jobs, IConfiguration configuration) : IChatTool
{
    public string Name => "job_list";
    public string Description =>
        "List background jobs of this conversation (running first-class: "
        + "status, pid, elapsed). Pass all=true to include finished/killed rows.";
    public string ParametersJson =>
        """{"type":"object","properties":{"all":{"type":"boolean","description":"Include terminal jobs (default: running only)"}}}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("jobs unavailable without a conversation");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.JobsEnabledKey))
        {
            return Refused("chat jobs are disabled");
        }

        var all = arguments.TryGetProperty("all", out var a) && a.ValueKind is JsonValueKind.True;
        var rows = await jobs.ListAsync(conversationId, activeOnly: !all, cancellationToken)
            .ConfigureAwait(false);
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            jobs = rows.Select(j => new
            {
                jobId = j.Id,
                j.Command,
                j.Status,
                j.ExitCode,
                j.Error,
                startedAt = j.StartedAt,
                finishedAt = j.FinishedAt,
                elapsedSeconds = j.StartedAt is { } s
                    ? (int)((j.FinishedAt ?? DateTime.UtcNow) - s).TotalSeconds
                    : (int?)null,
            }).ToList(),
        }));
    }

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}

/// <summary>SPEC-20261005 RF-002: tail of a job's captured stdout+stderr.</summary>
public sealed class JobOutputTool(IChatJobService jobs, IConfiguration configuration) : IChatTool
{
    public string Name => "job_output";
    public string Description =>
        "Read the tail of a background job's captured output (stdout+stderr).";
    public string ParametersJson =>
        """{"type":"object","properties":{"job_id":{"type":"string"},"tail_bytes":{"type":"integer","description":"Max bytes of tail (default 16k, cap 64k)"}},"required":["job_id"]}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("jobs unavailable without a conversation");
        }

        if (!arguments.TryGetProperty("job_id", out var id) || id.ValueKind != JsonValueKind.String)
        {
            return Refused("job_id is required");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.JobsEnabledKey))
        {
            return Refused("chat jobs are disabled");
        }

        var tailBytes = arguments.TryGetProperty("tail_bytes", out var t) && t.TryGetInt32(out var tv)
            ? tv
            : 16 * 1024;
        var output = await jobs.GetOutputAsync(conversationId, id.GetString() ?? string.Empty, tailBytes, cancellationToken)
            .ConfigureAwait(false);
        if (output is null)
        {
            return Refused($"job '{id.GetString()}' not found");
        }

        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            jobId = output.JobId,
            status = output.Status,
            exitCode = output.ExitCode,
            output = output.OutputTail,
        }));
    }

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}

/// <summary>SPEC-20261005 RF-002: SIGTERM→SIGKILL a running job.</summary>
public sealed class JobKillTool(IChatJobService jobs, IConfiguration configuration) : IChatTool
{
    public string Name => "job_kill";
    public string Description => "Terminate a running background job (kill tree).";
    public string ParametersJson =>
        """{"type":"object","properties":{"job_id":{"type":"string"}},"required":["job_id"]}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("jobs unavailable without a conversation");
        }

        if (!arguments.TryGetProperty("job_id", out var id) || id.ValueKind != JsonValueKind.String)
        {
            return Refused("job_id is required");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.JobsEnabledKey))
        {
            return Refused("chat jobs are disabled");
        }

        try
        {
            var job = await jobs.KillAsync(conversationId, id.GetString() ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            if (job is null)
            {
                return Refused($"job '{id.GetString()}' not found");
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                jobId = job.Id,
                status = job.Status,
            }));
        }
        catch (ChatJobConflictException ex)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = ex.Message }), Refused: true, ex.Message);
        }
    }

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}
