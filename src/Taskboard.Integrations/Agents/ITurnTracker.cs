using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Per-connection turn lifecycle tracker (SPEC-20260921-acp-v2-readiness
/// RF-204). v1: the <c>session/prompt</c> response carries
/// <c>stopReason</c> and ends the turn. v2: the response is only an ack
/// (<c>messageId</c>) — the turn ends when a <c>state_update</c> arrives with
/// <c>state == "idle"</c> and a <c>stopReason</c>.
/// </summary>
public interface ITurnTracker
{
    /// <summary>
    /// True when the <c>session/prompt</c> RPC response itself closes the
    /// turn (v1). False when it is merely an ack and the turn closes via a
    /// later notification (v2).
    /// </summary>
    bool PromptResponseEndsTurn { get; }

    /// <summary>Records the <c>session/prompt</c> response (v2 captures the ack <c>messageId</c>).</summary>
    void OnPromptResponse(JsonElement result);

    /// <summary>
    /// Inspects a <c>session/update</c> params object for turn end. Returns
    /// true with the <c>stopReason</c> when the update closes the turn
    /// (v2: <c>state_update</c> with <c>state == "idle"</c>).
    /// </summary>
    bool TryCompleteTurn(JsonElement updateParams, out string? stopReason);
}

/// <summary>v1 turn semantics: the prompt response ends the turn.</summary>
public sealed class AcpV1TurnTracker : ITurnTracker
{
    public bool PromptResponseEndsTurn => true;

    public void OnPromptResponse(JsonElement result)
    {
    }

    public bool TryCompleteTurn(JsonElement updateParams, out string? stopReason)
    {
        stopReason = null;
        return false;
    }
}

/// <summary>
/// v2 turn semantics: the prompt response is an ack; the turn closes on an
/// <c>idle</c> <c>state_update</c> carrying the terminal <c>stopReason</c>.
/// </summary>
public sealed class AcpV2TurnTracker : ITurnTracker
{
    /// <summary>The ack <c>messageId</c> from the prompt response, if the agent sent one.</summary>
    public string? MessageId { get; private set; }

    public bool PromptResponseEndsTurn => false;

    public void OnPromptResponse(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("messageId", out var mid)
            && mid.ValueKind == JsonValueKind.String)
        {
            MessageId = mid.GetString();
        }
    }

    public bool TryCompleteTurn(JsonElement updateParams, out string? stopReason)
    {
        stopReason = null;
        if (updateParams.ValueKind != JsonValueKind.Object
            || !updateParams.TryGetProperty("update", out var update)
            || update.ValueKind != JsonValueKind.Object
            || !update.TryGetProperty("sessionUpdate", out var su)
            || su.GetString() != "state_update"
            || !update.TryGetProperty("state", out var state)
            || state.GetString() != "idle")
        {
            return false;
        }

        stopReason = update.TryGetProperty("stopReason", out var sr) && sr.ValueKind == JsonValueKind.String
            ? sr.GetString()
            : null;
        return true;
    }
}
