using System.Text.Json;
using Microsoft.Extensions.Configuration;

using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Loads a globally-discovered skill's SKILL.md into the conversation
/// (SPEC-20261001-chat-skills-slash-commands FR-001) — the model then follows
/// its instructions. Read-only: returns content + resource listing, never
/// executes anything. Disabled skills refuse with the valid-name list.
/// </summary>
public sealed class SkillTool(ISkillDiscoveryService skills, IConfiguration configuration) : IChatTool
{
    private const int MaxInstructionsChars = 64_000;

    public string Name => "use_skill";
    public string Description =>
        "Load the instructions of a global skill (from ~/.claude, ~/.devin, ~/.cursor, "
        + "~/.opencode or built-in skills) and follow them for the current task.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "name":{"type":"string","description":"Skill name as listed in the system prompt"},
          "task_hint":{"type":"string","description":"What the skill should do (optional context)"}
        },"required":["name"]}
        """;

    public ChatCapabilityKind Kind => ChatCapabilityKind.Skill;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ChatToolResult(ErrorJson("name is required"), Refused: true, "empty name");
        }

        var discovered = await skills.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var enabled = discovered
            .Where(s => ChatCapabilityRules.IsEnabled(configuration, $"skill:{s.Name}"))
            .ToList();

        var matches = enabled
            .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            var valid = string.Join(", ", enabled.Select(s => s.Name).Order(StringComparer.OrdinalIgnoreCase).Take(50));
            return new ChatToolResult(
                ErrorJson($"skill '{name}' not found or disabled. Available: {valid}"),
                Refused: true, "skill not found");
        }

        var skill = matches[0];
        var detail = await skills.GetDetailAsync(skill.Source, skill.Name, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return new ChatToolResult(ErrorJson($"skill '{name}' content unavailable"), Refused: true, "skill unreadable");
        }

        var instructions = detail.Content.Length > MaxInstructionsChars
            ? string.Concat(detail.Content.AsSpan(0, MaxInstructionsChars), "\n\n(truncated)")
            : detail.Content;

        var resources = detail.Files
            .Select(f => f.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Take(50)
            .ToList();

        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            name = detail.Name,
            source = detail.Source,
            ambiguous = matches.Count > 1 ? matches.Select(m => m.Source).ToArray() : null,
            instructions,
            resources,
        }));
    }

    private static string ErrorJson(string message) => JsonSerializer.Serialize(new { error = message });
}
