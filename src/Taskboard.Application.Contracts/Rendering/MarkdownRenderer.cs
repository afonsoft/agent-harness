using Ganss.Xss;
using Markdig;

namespace Taskboard.Rendering;

/// <summary>
/// Renders GitHub-flavored markdown to sanitized HTML
/// (SPEC-20260918-kanban-card-ux RF-009). Issue bodies are untrusted content —
/// the output is safe to inject via <c>MarkupString</c>.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseTaskLists()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    /// <summary>Converts markdown to sanitized HTML; empty input → empty string.</summary>
    public static string ToSafeHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdown.ToHtml(markdown, Pipeline);
        return Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Add("input"); // task-list checkboxes (disabled)
        sanitizer.AllowedAttributes.Add("checked");
        sanitizer.AllowedAttributes.Add("type");
        sanitizer.AllowedAttributes.Add("disabled");
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("align");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.RemovingComment += (_, e) => { };
        return sanitizer;
    }
}
