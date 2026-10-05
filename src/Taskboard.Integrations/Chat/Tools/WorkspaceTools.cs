using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Surgical search/replace edit — the OpenCode <c>edit</c> tool: replaces a
/// unique <c>old_string</c> occurrence (or all of them with
/// <c>replace_all</c>) instead of rewriting the whole file. Path-jailed like
/// <see cref="WriteFileTool"/>.
/// </summary>
public sealed class EditFileTool(IChatFileEditTracker? editTracker = null) : IChatTool
{
    public string Name => "edit_file";
    public string Description =>
        "Edit a text file inside the workspace by replacing an exact string (path-jailed). "
        + "old_string must appear exactly once unless replace_all is true — include enough "
        + "surrounding context to make it unique. Prefer this over write_file for small changes.";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative path inside the workspace"},"old_string":{"type":"string","description":"Exact text to replace"},"new_string":{"type":"string","description":"Replacement text"},"replace_all":{"type":"boolean","description":"Replace every occurrence (default false)"}},"required":["path","old_string","new_string"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        var oldText = GetString(arguments, "old_string");
        var newText = GetString(arguments, "new_string");
        var replaceAll = arguments.TryGetProperty("replace_all", out var ra)
            && ra.ValueKind == JsonValueKind.True;
        if (oldText.Length == 0)
        {
            return new ChatToolResult(ErrorJson("old_string is required and cannot be empty"),
                Refused: true, "empty old_string");
        }

        try
        {
            var full = PathJailValidator.Validate(path, context.WorkspacePath);
            if (!File.Exists(full))
            {
                return new ChatToolResult(ErrorJson($"file not found: {path}"), RefusalReason: "file not found");
            }

            var content = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
            var occurrences = CountOccurrences(content, oldText);
            if (occurrences == 0)
            {
                return new ChatToolResult(
                    ErrorJson("old_string not found in the file — re-read the file for the exact text"),
                    RefusalReason: "no match");
            }

            if (occurrences > 1 && !replaceAll)
            {
                return new ChatToolResult(
                    ErrorJson($"old_string matches {occurrences} times — widen the context or pass replace_all"),
                    RefusalReason: "ambiguous match");
            }

            var updated = replaceAll
                ? content.Replace(oldText, newText, StringComparison.Ordinal)
                : content.Remove(content.IndexOf(oldText, StringComparison.Ordinal), oldText.Length)
                    .Insert(content.IndexOf(oldText, StringComparison.Ordinal), newText);

            // RF-007: register the mutation for the run's deliverables card.
            if (editTracker is not null && context.RunId is not null && context.ConversationId is not null)
            {
                editTracker.BeforeEdit(context.RunId, context.ConversationId, path, content);
                await File.WriteAllTextAsync(full, updated, cancellationToken).ConfigureAwait(false);
                editTracker.AfterEdit(context.RunId, context.ConversationId, path, updated);
            }
            else
            {
                await File.WriteAllTextAsync(full, updated, cancellationToken).ConfigureAwait(false);
            }
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                replacements = replaceAll ? occurrences : 1,
                bytes = updated.Length,
                edited = true,
            }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return new ChatToolResult(
                ErrorJson(ex.Message),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    internal static string GetString(JsonElement arguments, string property) =>
        arguments.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;

    internal static string ErrorJson(string message) =>
        JsonSerializer.Serialize(new { error = message });
}

/// <summary>
/// Content search — the OpenCode <c>grep</c> tool: regex over workspace text
/// files (path-jailed root), skipping binaries, heavy dirs and files above
/// 1&nbsp;MB. Returns <c>path:line: text</c> matches capped at 200.
/// </summary>
public sealed class SearchFilesTool(ISecretRedactor redactor) : IChatTool
{
    private const int MaxMatches = 200;
    private const long MaxFileBytes = 1_048_576;
    private static readonly string[] SkipDirs = [".git", "bin", "obj", "node_modules", ".vs", ".idea"];

    public string Name => "search_files";
    public string Description =>
        "Search file contents in the workspace with a regular expression (path-jailed). "
        + "Returns 'path:line: text' matches. Skips .git/bin/obj/node_modules and files > 1MB. "
        + "Options: include glob ('*.cs'), case_sensitive, path to scope the search.";
    public string ParametersJson => """
        {"type":"object","properties":{"pattern":{"type":"string","description":"Regular expression to search for"},"path":{"type":"string","description":"Relative directory or file to search (default: workspace root)"},"include":{"type":"string","description":"Optional glob filter on file names, e.g. '*.cs' or 'src/**.razor'"},"case_sensitive":{"type":"boolean","description":"Case-sensitive match (default false)"},"max_results":{"type":"integer","description":"Max matches to return (default 200)"}},"required":["pattern"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var pattern = EditFileTool.GetString(arguments, "pattern");
        if (pattern.Length == 0)
        {
            return new ChatToolResult(EditFileTool.ErrorJson("pattern is required"),
                Refused: true, "empty pattern");
        }

        Regex regex;
        try
        {
            var ignoreCase = !(arguments.TryGetProperty("case_sensitive", out var cs)
                && cs.ValueKind == JsonValueKind.True);
            regex = new Regex(pattern,
                RegexOptions.Compiled | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
                TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            return new ChatToolResult(EditFileTool.ErrorJson($"invalid regex: {ex.Message}"),
                Refused: true, "invalid regex");
        }

        var path = EditFileTool.GetString(arguments, "path");
        var include = EditFileTool.GetString(arguments, "include");
        var maxResults = arguments.TryGetProperty("max_results", out var m) && m.TryGetInt32(out var mv)
            ? Math.Clamp(mv, 1, 1000)
            : MaxMatches;
        var includeRegex = include.Length > 0 ? FindFilesTool.GlobToRegex(include) : null;

        try
        {
            var root = PathJailValidator.Validate(path.Length == 0 ? "." : path, context.WorkspacePath);
            var files = File.Exists(root)
                ? [root]
                : EnumerateFiles(root, includeRegex, cancellationToken);
            var matches = new List<object>();
            foreach (var file in files)
            {
                if (matches.Count >= maxResults || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await CollectMatchesAsync(file, root, regex, matches, maxResults, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                pattern,
                count = matches.Count,
                truncated = matches.Count >= maxResults,
                matches,
            }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return new ChatToolResult(
                EditFileTool.ErrorJson(ex.Message),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }

    private async Task CollectMatchesAsync(
        string file, string root, Regex regex, List<object> matches, int maxResults, CancellationToken ct)
    {
        var relative = Path.GetRelativePath(root, file);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            relative = Path.GetFileName(file);
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(file, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return; // binary/locked file — skip.
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        for (var i = 0; i < lines.Length && matches.Count < maxResults; i++)
        {
            if (regex.IsMatch(lines[i]))
            {
                var text = lines[i].Length > 400 ? lines[i][..400] + "…" : lines[i];
                matches.Add(new { path = relative.Replace('\\', '/'), line = i + 1, text = redactor.Redact(text) });
            }
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root, Regex? includeRegex, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && !ct.IsCancellationRequested)
        {
            var dir = pending.Pop();
            foreach (var entry in EnumerateEntries(dir))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (!SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        pending.Push(entry);
                    }
                }
                else if (IncludeFile(root, entry, name, includeRegex))
                {
                    yield return entry;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateEntries(string dir)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IncludeFile(string root, string entry, string name, Regex? includeRegex)
    {
        var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
        if (includeRegex is not null && !includeRegex.IsMatch(name) && !includeRegex.IsMatch(relative))
        {
            return false;
        }

        return new FileInfo(entry).Length <= MaxFileBytes;
    }
}

/// <summary>
/// Path search — the OpenCode <c>glob</c> tool: finds workspace files by glob
/// pattern (<c>**</c>, <c>*</c>, <c>?</c>, alternates like <c>*.{cs,razor}</c>),
/// path-jailed, skipping the same heavy dirs as <see cref="SearchFilesTool"/>.
/// </summary>
public sealed class FindFilesTool() : IChatTool
{
    private const int MaxResults = 500;

    public string Name => "find_files";
    public string Description =>
        "Find files in the workspace by glob pattern (path-jailed). Supports ** (any depth), "
        + "* (within a segment), ? and braces like '*.{cs,razor}'. Pattern matches the path "
        + "relative to the workspace root; a bare '*.cs' also matches nested files.";
    public string ParametersJson => """
        {"type":"object","properties":{"pattern":{"type":"string","description":"Glob pattern, e.g. '**/*.cs', 'src/**/*.razor' or '*.{json,yaml}'"},"path":{"type":"string","description":"Relative directory to search (default: workspace root)"},"max_results":{"type":"integer","description":"Max paths to return (default 500)"}},"required":["pattern"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var pattern = EditFileTool.GetString(arguments, "pattern");
        if (pattern.Length == 0)
        {
            return Task.FromResult(new ChatToolResult(EditFileTool.ErrorJson("pattern is required"),
                Refused: true, "empty pattern"));
        }

        var path = EditFileTool.GetString(arguments, "path");
        var maxResults = arguments.TryGetProperty("max_results", out var m) && m.TryGetInt32(out var mv)
            ? Math.Clamp(mv, 1, 5000)
            : MaxResults;
        var regex = GlobToRegex(pattern.Contains('/', StringComparison.Ordinal) ? pattern : $"**/{pattern}");

        try
        {
            var root = PathJailValidator.Validate(path.Length == 0 ? "." : path, context.WorkspacePath);
            var matches = SearchFilesTool.EnumerateFiles(root, includeRegex: null, cancellationToken)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(relative => regex.IsMatch(relative))
                .OrderBy(relative => relative, StringComparer.Ordinal)
                .Take(maxResults)
                .ToList();
            return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
            {
                pattern,
                count = matches.Count,
                truncated = matches.Count >= maxResults,
                matches,
            })));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ChatToolResult(
                EditFileTool.ErrorJson(ex.Message),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message));
        }
    }

    /// <summary>Converts a glob into a compiled regex over '/'-separated relative paths.</summary>
    internal static Regex GlobToRegex(string glob)
    {
        var builder = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            switch (glob[i])
            {
                case '*':
                    i += EmitStar(builder, glob, i);
                    break;
                case '{':
                    i += EmitAlternates(builder, glob, i);
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                case '/':
                    builder.Append('/');
                    break;
                default:
                    builder.Append(Regex.Escape(glob[i].ToString()));
                    break;
            }

            i++;
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
    }

    /// <summary>Appends the regex fragment for a '*' at index i; returns the extra chars consumed.</summary>
    private static int EmitStar(StringBuilder builder, string glob, int i)
    {
        if (i + 1 < glob.Length && glob[i + 1] == '*')
        {
            // '**/' spans directories; '**' alone matches everything.
            var withSlash = i + 2 < glob.Length && glob[i + 2] == '/';
            builder.Append(withSlash ? "(?:[^/]+/)*" : ".*");
            return withSlash ? 2 : 1;
        }

        builder.Append("[^/]*");
        return 0;
    }

    /// <summary>Appends the regex fragment for a '{a,b,c}' at index i; returns the extra chars consumed.</summary>
    private static int EmitAlternates(StringBuilder builder, string glob, int i)
    {
        var end = glob.IndexOf('}', i + 1);
        if (end < 0)
        {
            builder.Append("\\{");
            return 0;
        }

        var alternates = glob[(i + 1)..end]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Regex.Escape);
        builder.Append("(?:").Append(string.Join('|', alternates)).Append(')');
        return end - i;
    }
}

/// <summary>
/// Read-only git — the OpenCode repo awareness: <c>status</c>, <c>log</c>,
/// <c>diff</c> and <c>branch</c> against the workspace repository. Mutating
/// git subcommands are never exposed here — <c>shell_exec</c> stays the
/// gateway for anything that writes.
/// </summary>
public sealed class GitTool(ISecretRedactor redactor) : IChatTool
{
    public string Name => "git";
    public string Description =>
        "Read-only git info for the workspace repository (path-jailed). "
        + "Actions: status, log(max?), diff(path?), branch. Mutating git commands are refused.";
    public string ParametersJson => """
        {"type":"object","properties":{"action":{"type":"string","enum":["status","log","diff","branch"],"description":"Read-only git operation"},"path":{"type":"string","description":"Optional relative path to scope the diff/status"},"max":{"type":"integer","description":"Max log entries (default 20)"}},"required":["action"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var action = EditFileTool.GetString(arguments, "action");
        var path = EditFileTool.GetString(arguments, "path");
        var max = arguments.TryGetProperty("max", out var m) && m.TryGetInt32(out var mv)
            ? Math.Clamp(mv, 1, 200)
            : 20;

        string[] argv;
        switch (action)
        {
            case "status":
                argv = ["status", "--short", "--branch", .. ScopedPath(path)];
                break;
            case "log":
                argv = ["log", "--oneline", "--decorate", $"-{max}", .. ScopedPath(path)];
                break;
            case "diff":
                argv = ["diff", "--", .. ScopedPath(path, bare: true)];
                break;
            case "branch":
                argv = ["branch", "--show-current"];
                break;
            default:
                return new ChatToolResult(
                    EditFileTool.ErrorJson($"unknown action '{action}' — use status|log|diff|branch"),
                    Refused: true, "unknown action");
        }

        try
        {
            // Path-scoped queries resolve inside the jail — a path that would
            // escape never reaches git.
            if (path.Length > 0)
            {
                PathJailValidator.Validate(path, context.WorkspacePath);
            }

            var result = await ChatProcessRunner.RunAsync("git", argv, context.WorkspacePath,
                TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            var output = redactor.Redact(
                ChatProcessRunner.Truncate($"{result.Stdout}{result.Stderr}"));
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                action,
                exitCode = result.ExitCode,
                timedOut = result.TimedOut,
                output,
            }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException)
        {
            return new ChatToolResult(
                EditFileTool.ErrorJson(ex.Message),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }

    private static IEnumerable<string> ScopedPath(string path, bool bare = false)
    {
        if (path.Length == 0)
        {
            yield break;
        }

        if (bare)
        {
            yield return path;
        }
        else
        {
            yield return "--";
            yield return path;
        }
    }
}

/// <summary>
/// Runs the .NET test suite on a project/solution inside the workspace —
/// the OpenCode verify loop: <c>dotnet test</c> with an optional filter,
/// path-jailed, timeout-bounded and secret-scrubbed.
/// </summary>
public sealed class RunTestsTool(ISecretRedactor redactor) : IChatTool
{
    public string Name => "run_tests";
    public string Description =>
        "Run 'dotnet test' on a project or solution inside the workspace (path-jailed). "
        + "Returns exit code and truncated output. Options: filter (test name filter), "
        + "configuration (default Release), timeout_seconds (default 300, max 600).";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative path to the .sln/.csproj or test directory (default: workspace root — dotnet picks the solution)"},"filter":{"type":"string","description":"Optional --filter expression"},"configuration":{"type":"string","description":"Build configuration (default Release)"},"timeout_seconds":{"type":"integer","description":"Execution timeout (default 300, max 600)"}},"required":[]}
        """;

    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = EditFileTool.GetString(arguments, "path");
        var filter = EditFileTool.GetString(arguments, "filter");
        var configuration = EditFileTool.GetString(arguments, "configuration");
        if (configuration.Length == 0)
        {
            configuration = "Release";
        }

        if (configuration.IndexOfAny([' ', ';', '&', '|']) >= 0)
        {
            return new ChatToolResult(EditFileTool.ErrorJson("invalid configuration"),
                Refused: true, "invalid configuration");
        }

        var timeoutSeconds = arguments.TryGetProperty("timeout_seconds", out var t) && t.TryGetInt32(out var tv)
            ? Math.Clamp(tv, 30, 600)
            : 300;

        try
        {
            var argv = new List<string> { "test" };
            if (path.Length > 0)
            {
                var full = PathJailValidator.Validate(path, context.WorkspacePath);
                argv.Add(File.Exists(full) || Directory.Exists(full) ? full : path);
            }

            argv.Add("-c");
            argv.Add(configuration);
            argv.Add("--nologo");
            if (filter.Length > 0)
            {
                argv.Add("--filter");
                argv.Add(filter);
            }

            var result = await ChatProcessRunner.RunAsync("dotnet", argv, context.WorkspacePath,
                TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
            var output = redactor.Redact(
                ChatProcessRunner.Truncate($"{result.Stdout}{result.Stderr}"));
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                exitCode = result.ExitCode,
                timedOut = result.TimedOut,
                output,
            }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException)
        {
            return new ChatToolResult(
                EditFileTool.ErrorJson(ex.Message),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }
}

/// <summary>
/// Conversation task list — the OpenCode <c>todo</c> read/write tools: the
/// model breaks multi-step work into items (<c>write</c>) and re-reads the
/// live list (<c>list</c>). Items persist per conversation in
/// <see cref="ChatTodoStore"/>.
/// </summary>
public sealed class TodoTool(ChatTodoStore store) : IChatTool
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
        { "pending", "in_progress", "completed" };

    public string Name => "todo";
    public string Description =>
        "Track multi-step work for this conversation. Actions: list (current items), "
        + "write (replace the whole list — each item needs content and status "
        + "pending|in_progress|completed, optional id). Use it to keep progress "
        + "visible across turns.";
    public string ParametersJson => """
        {"type":"object","properties":{"action":{"type":"string","enum":["list","write"],"description":"Read or replace the task list"},"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"content":{"type":"string"},"status":{"type":"string","enum":["pending","in_progress","completed"]}},"required":["content","status"]},"description":"Full replacement list (write)"}},"required":["action"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var action = EditFileTool.GetString(arguments, "action");
        var conversationId = context.ConversationId ?? "default";
        return action switch
        {
            "list" => Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
            {
                items = store.List(conversationId),
            }))),
            "write" => Task.FromResult(WriteItems(arguments, conversationId)),
            _ => Task.FromResult(new ChatToolResult(
                EditFileTool.ErrorJson($"unknown action '{action}' — use list|write"),
                Refused: true, "unknown action")),
        };
    }

    private ChatToolResult WriteItems(JsonElement arguments, string conversationId)
    {
        if (!arguments.TryGetProperty("items", out var itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return new ChatToolResult(EditFileTool.ErrorJson("items array is required for write"),
                Refused: true, "missing items");
        }

        var items = new List<ChatTodoItem>();
        foreach (var entry in itemsElement.EnumerateArray())
        {
            var content = EditFileTool.GetString(entry, "content");
            var status = EditFileTool.GetString(entry, "status");
            if (content.Length == 0 || !ValidStatuses.Contains(status))
            {
                return new ChatToolResult(
                    EditFileTool.ErrorJson("each item needs content and status pending|in_progress|completed"),
                    Refused: true, "invalid item");
            }

            var id = EditFileTool.GetString(entry, "id");
            items.Add(new ChatTodoItem(
                id.Length > 0 ? id : $"t{items.Count + 1}",
                content,
                status));
        }

        store.Write(conversationId, items);
        return new ChatToolResult(JsonSerializer.Serialize(new { written = items.Count, items }));
    }
}
