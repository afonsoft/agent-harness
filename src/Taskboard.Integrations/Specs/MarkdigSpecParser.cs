using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Specs;

namespace Taskboard.Integrations.Specs;

/// <summary>
/// SDD spec parser over the two generations found in <c>.specs/</c>:
/// legacy (<c>## 0. SPEC Metadata</c>, <c>### FR-xxx</c>) and current
/// (<c>## 0. Metadata</c>, <c>### RF-xxx</c>, BDD criteria).
/// (SPEC-20260919-ade-living-specs RF-001.)
/// </summary>
public sealed partial class MarkdigSpecParser : ISpecDocumentParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    public LivingSpecification Parse(string filePath, string markdown)
    {
        var warnings = new List<SpecLintWarning>();
        var doc = Markdown.Parse(markdown, Pipeline);
        var blocks = doc.ToList();

        var sections = SplitSections(blocks);
        var metadata = ReadMetadata(blocks);

        var title = FirstHeadingText(blocks, 1)
            ?? Path.GetFileNameWithoutExtension(filePath);
        var rawStatus = GetMeta(metadata, "status");
        var status = NormalizeStatus(rawStatus, warnings);
        var date = DateOnly.TryParse(GetMeta(metadata, "date"), out var d) ? d : (DateOnly?)null;

        var requirements = new List<SpecRequirement>();
        var criteria = new List<string>();
        var tasks = new List<SpecTask>();
        var files = new List<string>();

        foreach (var section in sections)
        {
            CollectRequirements(section.Blocks, requirements);

            if (IsAcceptanceSection(section.Title))
            {
                CollectAcceptanceCriteria(section.Blocks, criteria);
            }

            if (IsTaskSection(section.Title))
            {
                CollectTasks(section.Blocks, tasks);
            }
        }

        CollectReferencedFiles(blocks, files);

        if (rawStatus is null)
        {
            warnings.Add(new SpecLintWarning("MISSING_METADATA", "No metadata table with a Status row was found."));
        }

        return new LivingSpecification(
            Path.GetFileNameWithoutExtension(filePath),
            filePath,
            title,
            GetMeta(metadata, "type") ?? GetMeta(metadata, "change type"),
            GetMeta(metadata, "stack") ?? GetMeta(metadata, "technical stack"),
            GetMeta(metadata, "branch") ?? GetMeta(metadata, "suggested branch"),
            GetMeta(metadata, "ticket"),
            status,
            rawStatus,
            date,
            requirements,
            criteria,
            tasks,
            files,
            warnings);
    }

    private sealed record Section(int Number, string Title, List<Block> Blocks);

    private static List<Section> SplitSections(List<Block> blocks)
    {
        var sections = new List<Section>();
        Section? current = null;
        foreach (var block in blocks)
        {
            if (block is HeadingBlock { Level: 2 } h)
            {
                var text = InlineText(h.Inline);
                var m = SectionNumberRegex().Match(text);
                current = new Section(
                    m.Success ? int.Parse(m.Groups[1].Value) : -1,
                    text,
                    []);
                sections.Add(current);
            }
            else if (current is not null)
            {
                current.Blocks.Add(block);
            }
        }
        return sections;
    }

    private static Dictionary<string, string> ReadMetadata(List<Block> blocks)
    {
        // The metadata table lives in section 0 but heading text varies
        // ("Metadata", "SPEC Metadata"); scan every table under a "## 0." heading.
        var inSectionZero = false;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            if (block is HeadingBlock { Level: 2 } h)
            {
                var text = InlineText(h.Inline);
                var m = SectionNumberRegex().Match(text);
                inSectionZero = m.Success && m.Groups[1].Value == "0";
                continue;
            }

            if (!inSectionZero || block is not Table table)
            {
                continue;
            }

            foreach (var row in table.OfType<TableRow>().Skip(1))
            {
                var cells = row.OfType<TableCell>()
                    .Select(c => InlineText((c.FirstOrDefault() as LeafBlock)?.Inline))
                    .ToList();
                if (cells.Count >= 2 && !string.IsNullOrWhiteSpace(cells[0]))
                {
                    result[cells[0].Trim()] = cells[1].Trim();
                }
            }
        }
        return result;
    }

    private static string? GetMeta(Dictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var v) ? UnwrapCode(v) : null;

    private static string UnwrapCode(string value) =>
        value.Trim().Trim('`').Trim();

    private static SpecStatus NormalizeStatus(string? raw, List<SpecLintWarning> warnings)
    {
        if (raw is null)
        {
            return SpecStatus.Draft;
        }

        // Keep only the leading phrase: "Done — merged via PR #94" → "done".
        var phrase = UnwrapCode(raw);
        var cut = phrase.IndexOfAny(['—', '–', '(', ';', ',']);
        var dashCut = phrase.IndexOf(" - ", StringComparison.Ordinal);
        if (dashCut >= 0 && (cut < 0 || dashCut < cut))
        {
            cut = dashCut;
        }
        if (cut >= 0)
        {
            phrase = phrase[..cut];
        }
        var key = StatusNoiseRegex().Replace(phrase, "").ToLowerInvariant();

        var status = key switch
        {
            "draft" => SpecStatus.Draft,
            "approved" or "aprovado" or "aprovada" => SpecStatus.Approved,
            "inimplementation" or "inprogress" or "implementacao" => SpecStatus.InImplementation,
            "done" or "implemented" or "completed" or "concluido" or "concluida" => SpecStatus.Done,
            "deprecated" or "deprecado" or "deprecada" or "obsolete" => SpecStatus.Deprecated,
            _ => (SpecStatus?)null
        };

        if (status is null)
        {
            warnings.Add(new SpecLintWarning("UNKNOWN_STATUS", $"Status '{raw}' is not a known lifecycle value."));
            return SpecStatus.Draft;
        }
        return status.Value;
    }

    private static void CollectRequirements(List<Block> blocks, List<SpecRequirement> requirements)
    {
        foreach (var block in blocks)
        {
            if (block is HeadingBlock { Level: >= 3 } h)
            {
                var m = RequirementRegex().Match(InlineText(h.Inline));
                if (m.Success)
                {
                    requirements.Add(new SpecRequirement(m.Groups[1].Value, CleanRequirementTitle(m.Groups[2].Value)));
                }
            }
            else if (block is ListBlock list)
            {
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var text = CheckboxRegex().Replace(FirstParagraphText(item), "").Trim();
                    var m = RequirementRegex().Match(text);
                    if (m.Success)
                    {
                        requirements.Add(new SpecRequirement(m.Groups[1].Value, CleanRequirementTitle(m.Groups[2].Value)));
                    }
                }
            }
        }
    }

    private static string CleanRequirementTitle(string raw) =>
        raw.Trim().TrimStart(':', '—', '–', '-', ' ').Trim();

    private static bool IsAcceptanceSection(string title) =>
        title.Contains("acceptance", StringComparison.OrdinalIgnoreCase)
        || title.Contains("critérios", StringComparison.OrdinalIgnoreCase)
        || title.Contains("criterios", StringComparison.OrdinalIgnoreCase);

    private static bool IsTaskSection(string title) =>
        title.Contains("task plan", StringComparison.OrdinalIgnoreCase)
        || title.Contains("task breakdown", StringComparison.OrdinalIgnoreCase)
        || title.Contains("plano de tarefas", StringComparison.OrdinalIgnoreCase);

    private static void CollectAcceptanceCriteria(List<Block> blocks, List<string> criteria)
    {
        foreach (var block in blocks)
        {
            if (block is not ListBlock list)
            {
                continue;
            }
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var text = CheckboxRegex().Replace(FirstParagraphText(item), "").Trim();
                if (text.Length > 0)
                {
                    criteria.Add(text);
                }
            }
        }
    }

    private static void CollectTasks(List<Block> blocks, List<SpecTask> tasks)
    {
        foreach (var block in blocks)
        {
            if (block is not ListBlock list)
            {
                continue;
            }
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var m = TaskCheckboxRegex().Match(FirstParagraphText(item).Trim());
                if (m.Success)
                {
                    tasks.Add(new SpecTask(
                        m.Groups[2].Value.Trim(),
                        m.Groups[1].Value.Equals("x", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
    }

    private static void CollectReferencedFiles(List<Block> blocks, List<string> files)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not HeadingBlock trigger
                || !InlineText(trigger.Inline).Contains("files to create or modify", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            for (var j = i + 1; j < blocks.Count; j++)
            {
                if (blocks[j] is HeadingBlock h && h.Level <= trigger.Level)
                {
                    break;
                }
                if (blocks[j] is FencedCodeBlock fenced)
                {
                    foreach (var line in fenced.Lines.Lines)
                    {
                        var token = line.ToString().Trim()
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault();
                        if (token is not null && LooksLikePath(token))
                        {
                            files.Add(token);
                        }
                    }
                }
                else if (blocks[j] is ListBlock list)
                {
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var token = CheckboxRegex()
                            .Replace(FirstParagraphText(item), "")
                            .Trim()
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault();
                        if (token is not null && LooksLikePath(token))
                        {
                            files.Add(token);
                        }
                    }
                }
            }
        }
    }

    private static bool LooksLikePath(string token) =>
        token.StartsWith('[') is false && token.Contains('/') && !token.StartsWith("http");

    private static string? FirstHeadingText(List<Block> blocks, int level) =>
        blocks.OfType<HeadingBlock>().FirstOrDefault(h => h.Level == level) is { } h
            ? InlineText(h.Inline)
            : null;

    private static string FirstParagraphText(ListItemBlock item) =>
        item.OfType<ParagraphBlock>().FirstOrDefault() is { } p ? InlineText(p.Inline) : "";

    private static string InlineText(Inline? inline)
    {
        var sb = new StringBuilder();
        for (var node = inline; node is not null; node = node.NextSibling)
        {
            AppendInlineText(node, sb);
        }
        return sb.ToString();
    }

    private static void AppendInlineText(Inline inline, StringBuilder sb)
    {
        if (inline is LiteralInline literal)
        {
            sb.Append(literal.Content.ToString());
        }
        else if (inline is CodeInline code)
        {
            sb.Append(code.Content.ToString());
        }
        if (inline is ContainerInline container)
        {
            for (var child = container.FirstChild; child is not null; child = child.NextSibling)
            {
                AppendInlineText(child, sb);
            }
        }
    }

    [GeneratedRegex(@"^(\d+)\.")]
    private static partial Regex SectionNumberRegex();

    [GeneratedRegex(@"^((?:RF|FR)-\d+)\b\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RequirementRegex();

    [GeneratedRegex(@"^\[( |x|X)\]\s*")]
    private static partial Regex CheckboxRegex();

    [GeneratedRegex(@"^\[( |x|X)\]\s*(.*)$")]
    private static partial Regex TaskCheckboxRegex();

    [GeneratedRegex(@"[\s_`\-]")]
    private static partial Regex StatusNoiseRegex();
}
