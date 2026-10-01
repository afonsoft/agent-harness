using System.Text.Json;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Render-ready view of an ACP tool call after merging the initial
/// <c>tool_call</c> update with its <c>tool_call_update</c>s
/// (SPEC-20260921-ai-code-chat-ux RF-001): <c>edit</c>/<c>write</c> → inline
/// diff, <c>execute</c>/<c>terminal</c> → collapsible output + exit code,
/// <c>read</c> → truncated preview, anything else → generic card.
/// </summary>
public sealed record ToolCallRenderModel(
    string? ToolCallId,
    string Title,
    string Kind,
    string Status,
    string? Path,
    string? OldText,
    string? NewText,
    string? Output,
    int? ExitCode,
    int AddedLines,
    int RemovedLines)
{
    /// <summary>Kinds that mutate files and may aggregate into a "changes" card.</summary>
    public bool IsFileEdit => Kind is "edit";

    /// <summary>Maximum rendered payload before collapsing (RF-006).</summary>
    public const int CollapseThreshold = 8 * 1024;

    /// <summary>
    /// Card for simple <c>{name,args,status,output}</c> payloads (cockpit and
    /// agent-run timelines) — renders through the generic fallback branch.
    /// </summary>
    public static ToolCallRenderModel Generic(string name, string? arguments, string? status, string? output)
    {
        string? combined;
        if (arguments is { Length: > 0 })
        {
            combined = output is { Length: > 0 } ? $"{arguments}\n{output}" : arguments;
        }
        else
        {
            combined = output;
        }

        return new ToolCallRenderModel(null, name, "other", status ?? "running", null, null, null, combined, null, 0, 0);
    }
}

public static class ToolCallRender
{
    /// <summary>
    /// Merges one <c>tool_call</c> payload with its <c>tool_call_update</c>
    /// payloads (in order) into a single render model. Either side may be
    /// null — an orphan update still yields a generic card.
    /// </summary>
    public static ToolCallRenderModel Parse(string? toolCallJson, IReadOnlyList<string?> updateJsons)
    {
        var acc = new Accumulator();

        if (TryParse(toolCallJson, out var call))
        {
            acc.ApplyCall(call);
        }

        foreach (var uj in updateJsons)
        {
            if (TryParse(uj, out var update))
            {
                acc.ApplyUpdate(update);
            }
        }

        return acc.ToModel();
    }

    private sealed class Accumulator
    {
        public string? ToolCallId;
        public string Title = "tool";
        public string? RawKind;
        public string Status = "pending";
        public string? Path;
        public string? OldText;
        public string? NewText;
        public string? Output;
        public int? ExitCode;
        public bool SawTerminal;

        public void ApplyCall(JsonElement el)
        {
            ToolCallId ??= Str(el, "toolCallId");
            Title = Str(el, "title") is { Length: > 0 } t ? t : Title;
            RawKind ??= Str(el, "kind");
            Status = Str(el, "status") ?? Status;
            Output ??= Str(el, "rawOutput");
            ApplyLocations(el);
            ApplyContentBlocks(el);
        }

        public void ApplyUpdate(JsonElement el)
        {
            ToolCallId ??= Str(el, "toolCallId");
            RawKind ??= Str(el, "kind");
            Status = Str(el, "status") ?? Status;
            if (Str(el, "rawOutput") is { } ro)
            {
                Output = ro;
            }

            if (el.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number)
            {
                ExitCode = ec.GetInt32();
            }
            else if (el.TryGetProperty("exitStatus", out var es) && es.ValueKind == JsonValueKind.Object
                && es.TryGetProperty("exitCode", out var nec) && nec.ValueKind == JsonValueKind.Number)
            {
                ExitCode = nec.GetInt32();
            }

            ApplyLocations(el);
            ApplyContentBlocks(el);
        }

        private void ApplyLocations(JsonElement el)
        {
            if (Path is not null
                || !el.TryGetProperty("locations", out var locs)
                || locs.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var loc in locs.EnumerateArray())
            {
                if (loc.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } path)
                {
                    Path = path;
                    return;
                }
            }
        }

        private void ApplyContentBlocks(JsonElement el)
        {
            if (!el.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var block in content.EnumerateArray())
            {
                var type = Str(block, "type");
                switch (type)
                {
                    case "diff":
                        Path ??= Str(block, "path");
                        OldText = Str(block, "oldText") ?? OldText;
                        NewText = Str(block, "newText") ?? NewText;
                        break;
                    case "terminal":
                        SawTerminal = true;
                        break;
                    case "content":
                        if (block.TryGetProperty("content", out var inner)
                            && inner.ValueKind == JsonValueKind.Object
                            && inner.TryGetProperty("text", out var text)
                            && text.GetString() is { } s)
                        {
                            Output = Output is null ? s : Output + "\n" + s;
                        }
                        break;
                }
            }
        }

        private string NormalizeKind()
        {
            var kind = RawKind?.ToLowerInvariant();
            if (kind is "edit" or "write" or "delete" or "move")
            {
                return "edit";
            }

            if (kind is "execute" or "terminal")
            {
                return "execute";
            }

            if (kind is "read")
            {
                return "read";
            }

            // Kind missing — infer from payload shape.
            if (kind is null)
            {
                if (OldText is not null || NewText is not null)
                {
                    return "edit";
                }

                if (SawTerminal || ExitCode is not null)
                {
                    return "execute";
                }
            }

            return "other";
        }

        public ToolCallRenderModel ToModel()
        {
            var kind = NormalizeKind();
            var (added, removed) = DiffCounts();
            return new ToolCallRenderModel(
                ToolCallId, Title, kind, Status, Path, OldText, NewText, Output, ExitCode,
                added, removed);
        }

        private (int Added, int Removed) DiffCounts()
        {
            var oldLines = SplitLines(OldText);
            var newLines = SplitLines(NewText);
            if (oldLines.Length == 0 && newLines.Length == 0)
            {
                return (0, 0);
            }

            // Prefix/suffix trim: the differing "middle" lines are the +a/-d count.
            var prefix = 0;
            var max = Math.Min(oldLines.Length, newLines.Length);
            while (prefix < max && oldLines[prefix] == newLines[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < max - prefix
                && oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix])
            {
                suffix++;
            }

            var removed = oldLines.Length - prefix - suffix;
            var added = newLines.Length - prefix - suffix;
            return (added, removed);
        }

        private static string[] SplitLines(string? text) =>
            string.IsNullOrEmpty(text) ? [] : text.Split('\n');

        private static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
    }

    private static bool TryParse(string? json, out JsonElement el)
    {
        el = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            el = JsonDocument.Parse(json).RootElement.Clone();
            return el.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// One item in the chat render list: either a passthrough event
/// (<see cref="Event"/>) or a tool card (<see cref="Tool"/> / <see cref="Group"/>).
/// </summary>
public sealed record ChatRenderItem(
    AiChatEventDto? Event = null,
    ToolCallRenderModel? Tool = null,
    IReadOnlyList<ToolCallRenderModel>? Group = null);

/// <summary>
/// Groups the flat <see cref="AiChatEvent"/> stream into render items
/// (SPEC-20260921-ai-code-chat-ux RF-001): <c>tool_call</c> +
/// <c>tool_output</c> merge by <c>toolCallId</c>; two or more consecutive
/// file-edit calls aggregate into one "changes" card.
/// </summary>
public static class ToolCallGrouper
{
    public static IReadOnlyList<ChatRenderItem> Build(IReadOnlyList<AiChatEventDto> events)
    {
        // First pass: merge each tool_call with its updates by toolCallId.
        var merged = new List<ChatRenderItem>();
        var byCallId = new Dictionary<string, ToolAccumulator>(StringComparer.Ordinal);
        var order = new List<ToolAccumulator>();

        foreach (var ev in events)
        {
            if (ev.Kind is not ("tool_call" or "tool_output"))
            {
                merged.Add(new ChatRenderItem(Event: ev));
                continue;
            }

            var callId = ExtractCallId(ev.PayloadJson);
            if (callId is not null && byCallId.TryGetValue(callId, out var existing))
            {
                // Out-of-order delivery: a late tool_call still plays the
                // "call" role in the merge, never an update.
                if (ev.Kind == "tool_call")
                {
                    existing.Call ??= ev.PayloadJson;
                }
                else
                {
                    existing.Updates.Add(ev.PayloadJson);
                }

                continue;
            }

            var acc = new ToolAccumulator(callId);
            if (ev.Kind == "tool_call")
            {
                acc.Call = ev.PayloadJson;
            }
            else
            {
                acc.Updates.Add(ev.PayloadJson);
            }

            if (callId is not null)
            {
                byCallId[callId] = acc;
            }

            order.Add(acc);
            merged.Add(new ChatRenderItem(Tool: null!)); // placeholder, fixed below
        }

        // Second pass: resolve placeholders into models and aggregate
        // consecutive file edits into changes groups.
        var result = new List<ChatRenderItem>();
        var accQueue = new Queue<ToolAccumulator>(order);
        List<ToolCallRenderModel>? pendingEdits = null;

        void FlushEdits()
        {
            if (pendingEdits is null)
            {
                return;
            }

            result.Add(pendingEdits.Count > 1
                ? new ChatRenderItem(Group: pendingEdits)
                : new ChatRenderItem(Tool: pendingEdits[0]));
            pendingEdits = null;
        }

        foreach (var item in merged)
        {
            if (item.Event is not null)
            {
                FlushEdits();
                result.Add(item);
                continue;
            }

            var acc = accQueue.Dequeue();
            var model = ToolCallRender.Parse(acc.Call, acc.Updates);
            if (model.IsFileEdit)
            {
                (pendingEdits ??= []).Add(model);
            }
            else
            {
                FlushEdits();
                result.Add(new ChatRenderItem(Tool: model));
            }
        }

        FlushEdits();
        return result;
    }

    private sealed class ToolAccumulator(string? callId)
    {
        public string? CallId { get; } = callId;
        public string? Call;
        public List<string?> Updates { get; } = [];
    }

    private static string? ExtractCallId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("toolCallId", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
