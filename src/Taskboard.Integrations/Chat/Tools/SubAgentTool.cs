using System.Text;
using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Dispatches an isolated read-only sub-agent on the same provider
/// (SPEC-20261001-chat-agent-delegation FR-002) — aaPanel-style <c>task</c>:
/// restricted toolset, bounded iterations, no recursion (neither
/// <c>task</c> nor <c>run_agent</c> enter the sub-agent tool set).
/// </summary>
public sealed class SubAgentTool(OpenAiCompatibleClient client) : IChatTool
{
    private const int MaxIterations = 4;

    /// <summary>Read-only whitelist — write/exec/delegation never allowed.</summary>
    private static readonly ISet<string> SubAgentTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "read_file", "list_dir", "web_search", "use_skill",
    };

    private const string ProfileExplore = "explore";

    private static readonly IReadOnlyDictionary<string, string> Profiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProfileExplore] = "You are a read-only exploration sub-agent. Inspect the workspace "
                + "with the available tools and answer the question concisely with file/line "
                + "references. You cannot modify anything.",
            ["review"] = "You are a read-only review sub-agent. Examine the referenced "
                + "artifacts/diffs and report concrete problems, risks and suggestions. "
                + "You cannot modify anything.",
            ["summarize"] = "You are a read-only summarization sub-agent. Read the "
                + "referenced logs/documents and return a concise structured summary. "
                + "You cannot modify anything.",
        };

    public string Name => "task";
    public string Description =>
        "Dispatch a read-only sub-agent to explore/review/summarize on its own and return "
        + "an answer. Use for focused inspection that would clutter this conversation.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Task for the sub-agent"},
          "profile":{"type":"string","enum":["explore","review","summarize","custom"],"description":"Built-in profile (default explore)"},
          "system":{"type":"string","description":"Custom system prompt (only with profile=custom)"}
        },"required":["prompt"]}
        """;

    public string CapabilityId => "agent:task";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.DelegationDepth > 0)
        {
            return new ChatToolResult(ErrorJson("sub-agents cannot spawn sub-agents"), Refused: true, "recursion blocked");
        }

        var prompt = arguments.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ChatToolResult(ErrorJson("prompt is required"), Refused: true, "empty prompt");
        }

        var profile = arguments.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.String
            ? pr.GetString() ?? ProfileExplore
            : ProfileExplore;
        var customSystem = arguments.TryGetProperty("system", out var sy) && sy.ValueKind == JsonValueKind.String
            ? sy.GetString()
            : null;

        var systemPrompt = profile == "custom" && !string.IsNullOrWhiteSpace(customSystem)
            ? customSystem!
            : Profiles.GetValueOrDefault(profile, Profiles[ProfileExplore]);

        // Effective set ∩ read-only whitelist; recursion tools never included.
        var toolSet = (context.ToolSet ?? new Dictionary<string, IChatTool>())
            .Where(kv => SubAgentTools.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var toolDefs = toolSet
            .Select(kv => new OpenAiToolDefinition(kv.Key, kv.Value.Description, kv.Value.ParametersJson))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var wire = new List<OpenAiChatMessage>
        {
            new("system", $"{systemPrompt}\nWorkspace: {context.WorkspacePath}"),
            new("user", prompt),
        };

        context.Activity?.Report("running_subagent", profile);

        var toolsUsed = new List<string>();
        var answer = new StringBuilder();
        var iterations = 0;

        try
        {
            for (; iterations < MaxIterations;)
            {
                var (round, toolCalls) = await StreamRoundAsync(context, wire, toolDefs, cancellationToken)
                    .ConfigureAwait(false);
                answer.Clear().Append(round);
                iterations++;

                if (toolCalls.Count == 0)
                {
                    break;
                }

                wire.Add(new OpenAiChatMessage("assistant", round.ToString(), toolCalls));
                await ExecuteToolCallsAsync(toolCalls, toolSet, context, wire, toolsUsed, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (ChatProviderException ex)
        {
            return new ChatToolResult(ErrorJson($"sub-agent provider error: {ex.Message}"));
        }

        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            answer = answer.ToString(),
            iterations,
            tools_used = toolsUsed,
            truncated = iterations >= MaxIterations,
        }));
    }

    /// <summary>One provider round: streams deltas and materializes tool calls.</summary>
    private async Task<(StringBuilder Round, List<OpenAiToolCall> ToolCalls)> StreamRoundAsync(
        ChatToolContext context,
        List<OpenAiChatMessage> wire,
        List<OpenAiToolDefinition> toolDefs,
        CancellationToken cancellationToken)
    {
        var round = new StringBuilder();
        var toolAccumulator = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)>();

        await foreach (var chunk in client.StreamChatAsync(
                context.ProviderBaseUrl, context.ProviderApiKey,
                context.Model ?? string.Empty, wire,
                toolDefs.Count > 0 ? toolDefs : null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            AccumulateChunk(chunk, round, toolAccumulator);
        }

        var toolCalls = toolAccumulator
            .Select(MaterializeToolCall)
            .ToList();
        return (round, toolCalls);
    }

    private static void AccumulateChunk(
        OpenAiStreamEvent chunk,
        StringBuilder round,
        SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)> toolAccumulator)
    {
        if (chunk.ContentDelta is { Length: > 0 } delta)
        {
            round.Append(delta);
        }

        foreach (var tc in chunk.ToolCallDeltas ?? [])
        {
            var current = toolAccumulator.TryGetValue(tc.Index, out var v)
                ? v
                : (null, null, new StringBuilder());
            toolAccumulator[tc.Index] = (
                tc.Id ?? current.Item1,
                tc.Name ?? current.Item2,
                current.Item3.Append(tc.ArgumentsDelta));
        }
    }

    private static OpenAiToolCall MaterializeToolCall(
        KeyValuePair<int, (string? Id, string? Name, StringBuilder Args)> kv) =>
        new(
            kv.Value.Id ?? $"call_{kv.Key}",
            kv.Value.Name ?? "unknown",
            kv.Value.Args.Length == 0 ? "{}" : kv.Value.Args.ToString());

    private static async Task ExecuteToolCallsAsync(
        List<OpenAiToolCall> toolCalls,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatToolContext context,
        List<OpenAiChatMessage> wire,
        List<string> toolsUsed,
        CancellationToken cancellationToken)
    {
        foreach (var call in toolCalls)
        {
            var (json, _, _) = await ExecuteSubToolAsync(call, toolSet, context, cancellationToken)
                .ConfigureAwait(false);
            toolsUsed.Add(call.Name);
            wire.Add(new OpenAiChatMessage("tool", json, ToolCallId: call.Id, Name: call.Name));
        }
    }

    private static async Task<(string Json, bool Refused, string? Reason)> ExecuteSubToolAsync(
        OpenAiToolCall call,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatToolContext context,
        CancellationToken ct)
    {
        if (!toolSet.TryGetValue(call.Name, out var tool))
        {
            return (ErrorJson($"tool '{call.Name}' not allowed for sub-agents"), true, "not allowed");
        }

        JsonElement toolArgs;
        try
        {
            toolArgs = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson);
        }
        catch (JsonException)
        {
            return (ErrorJson("invalid tool arguments"), true, "bad arguments");
        }

        try
        {
            var subContext = context with { DelegationDepth = context.DelegationDepth + 1 };
            var result = await tool.ExecuteAsync(toolArgs, subContext, ct).ConfigureAwait(false);
            return (result.Json, result.Refused, result.RefusalReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (ErrorJson(ex.Message), false, null);
        }
    }

    private static string ErrorJson(string message) => JsonSerializer.Serialize(new { error = message });
}
