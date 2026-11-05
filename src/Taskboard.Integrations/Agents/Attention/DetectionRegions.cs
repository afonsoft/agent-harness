using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents.Attention;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-003: region extraction over the
/// detection snapshot. Screen regions from herdr's schema are approximated
/// against the bounded tail — there is no prompt-box geometry, so the last
/// non-empty line stands in for the prompt box. Unsupported/unknown regions
/// yield empty text (rules with required matchers then fail) plus an
/// <c>unknown-region:</c> warning for explain.
/// </summary>
public static class DetectionRegions
{
    // Horizontal rule lines: ─/━/═ or plain dashes, 3+ chars.
    private static readonly Regex HorizontalRule = new(
        @"^\s*[─━═-]{3,}[^\r\n]*$",
        RegexOptions.Compiled | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));

    // Prompt markers used by codex (›) and claude (❯) input lines.
    private static readonly Regex PromptMarker = new(
        @"^\s*[›❯][^\r\n]*$",
        RegexOptions.Compiled | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));

    private static readonly Regex RegionArgs = new(
        @"^([a-z_]+)\((\d+)\)$",
        RegexOptions.Compiled | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));

    /// <summary>Extracts the named region; returns "" when empty/unsupported.</summary>
    public static string Extract(DetectionSnapshot snapshot, string? spec, out string? warning)
    {
        warning = null;
        var trimmed = (spec ?? "whole_recent").Trim();
        var content = snapshot.Content;

        switch (trimmed)
        {
            case "osc_title":
                return snapshot.OscTitle ?? string.Empty;
            case "whole_recent":
            case "full_buffer":
                return content;
        }

        var args = RegionArgs.Match(trimmed);
        if (args.Success)
        {
            var n = int.Parse(args.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return args.Groups[1].Value switch
            {
                "bottom_non_empty_lines" => EdgeLines(content, n, fromBottom: true),
                "top_non_empty_lines" => EdgeLines(content, n, fromBottom: false),
                _ => Unknown(trimmed, out warning),
            };
        }

        return trimmed switch
        {
            "after_last_horizontal_rule" => AfterLastMarker(content, HorizontalRule),
            "after_last_prompt_marker"
                or "after_current_prompt_block_marker" => AfterLastMarker(content, PromptMarker),
            "before_current_prompt_marker"
                or "whole_recent_without_current_prompt_marker" =>
                BeforeLastMarker(content, PromptMarker),
            "current_prompt_block_marker" => LastMarkerText(content, PromptMarker),
            "prompt_box_body" => LastNonEmptyLine(content),
            "above_prompt_box" => AboveLastNonEmptyLine(content),
            "last_non_empty_above_prompt_box" => LastNonEmptyLine(AboveLastNonEmptyLine(content)),
            _ => Unknown(trimmed, out warning),
        };
    }

    private static string Unknown(string spec, out string? warning)
    {
        warning = $"unknown-region:{spec}";
        return string.Empty;
    }

    private static string EdgeLines(string content, int count, bool fromBottom)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fromBottom
            ? string.Join('\n', lines.TakeLast(count))
            : string.Join('\n', lines.Take(count));
    }

    /// <summary>Offset of the last line matching <paramref name="marker"/>, or -1.</summary>
    private static int LastMarkerOffset(string content, Regex marker)
    {
        var found = -1;
        var pos = 0;
        while (pos <= content.Length)
        {
            var end = content.IndexOf('\n', pos);
            var len = (end < 0 ? content.Length : end) - pos;
            var line = content.AsSpan(pos, len).TrimEnd('\r');
            if (marker.IsMatch(line))
            {
                found = pos;
            }
            if (end < 0)
            {
                break;
            }
            pos = end + 1;
        }
        return found;
    }

    private static string AfterLastMarker(string content, Regex marker)
    {
        var start = LastMarkerOffset(content, marker);
        if (start < 0)
        {
            return string.Empty;
        }
        var end = content.IndexOf('\n', start);
        return end < 0 ? string.Empty : content[(end + 1)..];
    }

    private static string BeforeLastMarker(string content, Regex marker)
    {
        var start = LastMarkerOffset(content, marker);
        return start <= 0 ? string.Empty : content[..start];
    }

    private static string LastMarkerText(string content, Regex marker)
    {
        var start = LastMarkerOffset(content, marker);
        if (start < 0)
        {
            return string.Empty;
        }
        var end = content.IndexOf('\n', start);
        return content[start..(end < 0 ? content.Length : end)].TrimEnd('\r');
    }

    private static string LastNonEmptyLine(string content)
    {
        var start = LastNonEmptyOffset(content);
        if (start < 0)
        {
            return string.Empty;
        }
        var end = content.IndexOf('\n', start);
        return content[start..(end < 0 ? content.Length : end)].TrimEnd('\r');
    }

    private static int LastNonEmptyOffset(string content)
    {
        var found = -1;
        var pos = 0;
        while (pos <= content.Length)
        {
            var end = content.IndexOf('\n', pos);
            var len = (end < 0 ? content.Length : end) - pos;
            if (!content.AsSpan(pos, len).IsWhiteSpace())
            {
                found = pos;
            }
            if (end < 0)
            {
                break;
            }
            pos = end + 1;
        }
        return found;
    }

    private static string AboveLastNonEmptyLine(string content)
    {
        var start = LastNonEmptyOffset(content);
        return start <= 0 ? string.Empty : content[..start];
    }
}
