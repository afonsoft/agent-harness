using System.Diagnostics;
using Taskboard.Agents;

namespace Taskboard.Harness;

/// <summary>
/// Activity source `Taskboard.Harness` with span factories for the harness
/// lifecycle (SPEC-20260919-ade-observability-finops RF-004). Lives in
/// Domain.Shared so both Application (pipeline engine) and Integrations
/// (orchestrator, verification) can instrument without cross-layer deps.
/// Spans become no-ops unless a listener is attached — nothing leaves the
/// process without explicit configuration.
/// </summary>
public static class HarnessTelemetrySource
{
    public const string Name = "Taskboard.Harness";

    public static readonly ActivitySource Source = new(Name, "1.0.0");

    public const string RunIdTag = "harness.run_id";
    public const string AgentTypeTag = "agent.type";
    public const string ModelNameTag = "model.name";
    public const string StageKeyTag = "harness.stage_key";
    public const string ToolNameTag = "harness.tool_name";
    public const string TokensTotalTag = "tokens.total";
    public const string TokensInputTag = "tokens.input";
    public const string TokensOutputTag = "tokens.output";
    public const string TokensCacheTag = "tokens.cache";
    public const string CostUsdTag = "cost.usd";

    public static Activity? StartRunSpan(string runId, AgentType agentType, string? modelName)
    {
        var activity = Source.StartActivity("harness.run", ActivityKind.Internal);
        TagRun(activity, runId, agentType, modelName);
        return activity;
    }

    public static Activity? StartStageSpan(string runId, string stageKey, AgentType? agentType, string? modelName)
    {
        var activity = Source.StartActivity("harness.stage", ActivityKind.Internal);
        TagRun(activity, runId, agentType, modelName);
        activity?.SetTag(StageKeyTag, stageKey);
        return activity;
    }

    public static Activity? StartToolCallSpan(string runId, string toolName, AgentType? agentType, string? modelName)
    {
        var activity = Source.StartActivity("harness.tool_call", ActivityKind.Internal);
        TagRun(activity, runId, agentType, modelName);
        activity?.SetTag(ToolNameTag, toolName);
        return activity;
    }

    public static Activity? StartVerificationSpan(string runId, AgentType? agentType, string? modelName)
    {
        var activity = Source.StartActivity("harness.verification", ActivityKind.Internal);
        TagRun(activity, runId, agentType, modelName);
        return activity;
    }

    /// <summary>Stamps token/cost attributes on a span (RF-004).</summary>
    public static void RecordUsage(Activity? activity, TokenUsage usage, decimal costUsd)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(TokensTotalTag, usage.TotalTokens);
        activity.SetTag(TokensInputTag, usage.InputTokens);
        activity.SetTag(TokensOutputTag, usage.OutputTokens);
        activity.SetTag(TokensCacheTag, usage.CacheTokens);
        activity.SetTag(CostUsdTag, costUsd);
    }

    private static void TagRun(Activity? activity, string runId, AgentType? agentType, string? modelName)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(RunIdTag, runId);
        if (agentType.HasValue)
        {
            activity.SetTag(AgentTypeTag, agentType.Value.ToString());
        }

        if (!string.IsNullOrEmpty(modelName))
        {
            activity.SetTag(ModelNameTag, modelName);
        }
    }
}
