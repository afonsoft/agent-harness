using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// Incremental filter + parser for tool-call markup some models emit inline in
/// <c>delta.content</c> instead of structured <c>tool_calls</c> — DeepSeek DSML
/// (<c>&lt;｜DSML｜function_calls&gt;</c>, fullwidth or ASCII pipes) and the
/// Hermes/Qwen <c>&lt;tool_call&gt;{json}&lt;/tool_call&gt;</c> envelope.
/// <see cref="Feed"/> returns only the visible portion of each delta (the raw
/// markup never reaches the UI or the persisted message); <see cref="Flush"/>
/// drains the tail at stream end, and <see cref="MaterializeCalls"/> converts
/// the captured blocks into real <see cref="OpenAiToolCall"/>s so the tool loop
/// executes them (SPEC-20261001-ai-chat-openwebui).
/// </summary>
public sealed class InlineToolCallMarkup
{
    /// <summary>Pathological guard — a block longer than this is emitted as text.</summary>
    private const int MaxBlockChars = 128 * 1024;

    private static readonly (string Opener, string[] Closers)[] Markers =
    [
        ("<｜DSML｜function_calls>", ["</｜DSML｜function_calls>", "</|DSML|function_calls>"]),
        ("<|DSML|function_calls>", ["</|DSML|function_calls>", "</｜DSML｜function_calls>"]),
        ("<tool_call>", ["</tool_call>"]),
    ];

    private static readonly Regex InvokePattern = new(
        """<[｜|]DSML[｜|]invoke\s+name="(?<name>[^"]+)"\s*>(?<body>.*?)</[｜|]DSML[｜|]invoke>""",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex ParameterPattern = new(
        """<[｜|]DSML[｜|]parameter\s+name="(?<name>[^"]+)"(?<attrs>[^>]*)>(?<value>.*?)</[｜|]DSML[｜|]parameter>""",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex StringAttrPattern = new(
        "string=\"(?<v>true|false)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly StringBuilder _pending = new();
    private readonly List<(string Body, bool IsDsml)> _blocks = [];
    private string[]? _activeClosers;

    /// <summary>Returns the visible portion of <paramref name="delta"/> (may be empty).</summary>
    public string Feed(string delta)
    {
        _pending.Append(delta);
        var visible = new StringBuilder();
        while (true)
        {
            var text = _pending.ToString();
            if (_activeClosers is not null)
            {
                var (closeAt, closer) = IndexOfCloser(text, _activeClosers);
                if (closeAt < 0)
                {
                    if (text.Length > MaxBlockChars)
                    {
                        // Pathological: never-ending block — emit verbatim so the
                        // answer is not swallowed forever.
                        visible.Append(text);
                        _pending.Clear();
                        _activeClosers = null;
                    }

                    break;
                }

                var end = closeAt + closer.Length;
                _blocks.Add((text[..end], IsDsml: closer.Contains("DSML", StringComparison.Ordinal)));
                _pending.Remove(0, end);
                _activeClosers = null;
                continue;
            }

            var markerAt = IndexOfAny(text, Markers.Select(m => m.Opener));
            if (markerAt >= 0)
            {
                visible.Append(text[..markerAt]);
                var opener = Markers.First(m => text.AsSpan(markerAt).StartsWith(m.Opener, StringComparison.Ordinal));
                _pending.Remove(0, markerAt + opener.Opener.Length);
                _activeClosers = opener.Closers;
                continue;
            }

            var hold = PrefixHold(text);
            var emit = text.Length - hold;
            if (emit > 0)
            {
                visible.Append(text.AsSpan(0, emit));
                _pending.Remove(0, emit);
            }

            break;
        }

        return visible.ToString();
    }

    /// <summary>
    /// Drains the tail at stream end. Held-back marker prefixes and an
    /// unterminated block are dropped — partial markup must never render.
    /// </summary>
    public string Flush()
    {
        var rest = _pending.ToString();
        _pending.Clear();
        var inBlock = _activeClosers is not null;
        _activeClosers = null;
        return inBlock || PrefixHold(rest) > 0 ? string.Empty : rest;
    }

    /// <summary>Synthesizes tool calls from the captured blocks (may be empty).</summary>
    public IReadOnlyList<OpenAiToolCall> MaterializeCalls()
    {
        var calls = new List<OpenAiToolCall>();
        foreach (var (body, isDsml) in _blocks)
        {
            if (isDsml)
            {
                calls.AddRange(ParseDsml(body));
            }
            else
            {
                var call = ParseToolCallEnvelope(body);
                if (call is not null)
                {
                    calls.Add(call);
                }
            }
        }

        return calls;
    }

    /// <summary>Strips markup blocks from already-accumulated text (persisted messages, retries).</summary>
    public static string StripBlocks(string content)
    {
        if (!MayContainMarkup(content))
        {
            return content;
        }

        var filter = new InlineToolCallMarkup();
        var visible = filter.Feed(content);
        return visible + filter.Flush();
    }

    /// <summary>Extracts tool calls from already-accumulated text (same path as streaming).</summary>
    public static IReadOnlyList<OpenAiToolCall> ExtractCalls(string content)
    {
        var filter = new InlineToolCallMarkup();
        filter.Feed(content);
        filter.Flush();
        return filter.MaterializeCalls();
    }

    internal static bool MayContainMarkup(string content) =>
        content.Contains("DSML", StringComparison.Ordinal)
        || content.Contains("<tool_call>", StringComparison.Ordinal);

    // ---- internals ----

    private static int IndexOfAny(string text, IEnumerable<string> needles)
    {
        return needles
            .Select(needle => text.IndexOf(needle, StringComparison.Ordinal))
            .Where(at => at >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static (int Index, string Closer) IndexOfCloser(string text, string[] closers)
    {
        var best = -1;
        var hit = string.Empty;
        foreach (var closer in closers)
        {
            var at = text.IndexOf(closer, StringComparison.Ordinal);
            if (at >= 0 && (best < 0 || at < best))
            {
                best = at;
                hit = closer;
            }
        }

        return (best, hit);
    }

    /// <summary>
    /// Length of the longest suffix of <paramref name="text"/> that is a proper
    /// prefix of a marker opener — that tail is held back until the next delta
    /// proves or disproves the marker.
    /// </summary>
    private static int PrefixHold(string text)
    {
        var hold = 0;
        foreach (var (opener, _) in Markers)
        {
            var max = Math.Min(opener.Length - 1, text.Length);
            for (var len = max; len > hold; len--)
            {
                if (text.AsSpan(text.Length - len).SequenceEqual(opener.AsSpan(0, len)))
                {
                    hold = len;
                    break;
                }
            }
        }

        return hold;
    }

    private static IEnumerable<OpenAiToolCall> ParseDsml(string block) =>
        InvokePattern.Matches(block)
            .Cast<Match>()
            .Select((invoke, index) => new OpenAiToolCall(
                $"inline_{index}",
                NormalizeName(invoke.Groups["name"].Value),
                BuildArguments(invoke.Groups["body"].Value)));

    private static OpenAiToolCall? ParseToolCallEnvelope(string block)
    {
        var json = block.EndsWith("</tool_call>", StringComparison.Ordinal)
            ? block[..^"</tool_call>".Length]
            : block;
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var arguments = "{}";
            if (root.TryGetProperty("arguments", out var argsEl))
            {
                arguments = argsEl.ValueKind == JsonValueKind.String
                    ? argsEl.GetString() ?? "{}"
                    : argsEl.GetRawText();
            }

            return new OpenAiToolCall("inline_0", NormalizeName(nameEl.GetString() ?? "unknown"), arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsStringParam(string attrs) =>
        !StringAttrPattern.IsMatch(attrs) || StringAttrPattern.Match(attrs).Groups["v"].Value == "true";

    /// <summary>Models sometimes emit namespaced names (functions.x, tools.x) — keep the last segment.</summary>
    private static string NormalizeName(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static string BuildArguments(string invokeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value, isString) in ParameterPattern.Matches(invokeBody)
                .Cast<Match>()
                .Select(parameter => (
                    parameter.Groups["name"].Value,
                    parameter.Groups["value"].Value,
                    IsStringParam(parameter.Groups["attrs"].Value))))
            {
                writer.WritePropertyName(name);
                if (!isString && TryWriteRaw(writer, value))
                {
                    continue;
                }

                writer.WriteStringValue(value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Non-string DSML parameters carry raw JSON (number, bool, object, array).</summary>
    private static bool TryWriteRaw(Utf8JsonWriter writer, string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            doc.RootElement.WriteTo(writer);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
