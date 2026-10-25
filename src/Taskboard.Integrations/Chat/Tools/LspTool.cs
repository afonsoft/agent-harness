using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Lsp;
using Taskboard.Integrations.Harness.Security;
using Taskboard.Harness;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261025 C4: language-server queries over the workspace —
/// <c>document_symbols|workspace_symbols|goto_definition|find_references|
/// diagnostics</c> via a pooled <c>csharp-ls</c> session (configurable per
/// <see cref="LspClientPool"/>). Deferred-catalog citizen: the schema ships
/// only after <c>tool_activate("lsp")</c>; the server stays a per-workspace
/// idle-reaped process.
/// </summary>
public sealed class LspTool(
    ILspClientPool pool,
    SensitivePathDenylist? denylist = null) : IChatTool
{
    private const int MaxResults = 50;

    public string Name => "lsp";
    public string Description =>
        "Query the language server bound to this workspace (csharp-ls): document_symbols lists "
        + "a file's symbols, workspace_symbols searches by name, goto_definition/find_references "
        + "jump by 0-based line/character, diagnostics lists a file's problems. Paths are "
        + "workspace-relative and path-jailed.";
    public string ParametersJson => """
        {"type":"object","properties":{"action":{"type":"string","enum":["goto_definition","find_references","document_symbols","workspace_symbols","diagnostics"],"description":"LSP query to run"},"path":{"type":"string","description":"Workspace-relative file path (required for file actions)"},"line":{"type":"integer","description":"0-based line (goto_definition/find_references)"},"character":{"type":"integer","description":"0-based character"},"query":{"type":"string","description":"Symbol name filter (workspace_symbols)"}},"required":["action"]}
        """;

    /// <summary>SPEC-20261023 RF-D01: server must resolve on PATH — otherwise the tool defers.</summary>
    public Task<bool> IsAvailableAsync(ChatToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(pool.IsServerAvailable());

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var action = arguments.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString() ?? string.Empty
            : string.Empty;

        string method;
        object parameters;
        try
        {
            (method, parameters) = BuildRequest(action, arguments, context.WorkspacePath);
        }
        catch (Exception ex) when (ex is ChatValidationException or SecurityAccessDeniedException)
        {
            return new ChatToolResult(ErrorJson(ex.Message), Refused: true, ex.Message);
        }

        var client = await pool.GetAsync(context.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return new ChatToolResult(
                ErrorJson("language server unavailable — is the configured LSP server installed?"),
                Refused: true, "lsp unavailable");
        }

        var result = await client.RequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        if (result is not { } doc || doc.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { action, results = Array.Empty<object>() }));
        }

        var normalized = action switch
        {
            "document_symbols" => NormalizeSymbols(doc),
            "goto_definition" or "find_references" => NormalizeLocations(doc),
            "workspace_symbols" => NormalizeSymbolInformation(doc),
            "diagnostics" => NormalizeDiagnostics(doc),
            _ => Array.Empty<object>(), // action validated in BuildRequest
        };
        return new ChatToolResult(JsonSerializer.Serialize(new { action, results = normalized }));
    }

    private (string Method, object Parameters) BuildRequest(
        string action, JsonElement arguments, string workspace)
    {
        switch (action)
        {
            case "workspace_symbols":
                var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
                return ("workspace/symbol", new { query });

            case "document_symbols":
                return ("textDocument/documentSymbol", new { textDocument = new { uri = FileUri(arguments, workspace) } });

            case "diagnostics":
                return ("textDocument/diagnostic", new { textDocument = new { uri = FileUri(arguments, workspace) } });

            case "goto_definition":
            case "find_references":
                var uri = FileUri(arguments, workspace);
                var position = new
                {
                    line = arguments.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : 0,
                    character = arguments.TryGetProperty("character", out var c) && c.TryGetInt32(out var ch) ? ch : 0,
                };
                return action == "goto_definition"
                    ? ("textDocument/definition", new { textDocument = new { uri }, position })
                    : ("textDocument/references", new
                    {
                        textDocument = new { uri },
                        position,
                        context = new { includeDeclaration = true },
                    });

            default:
                throw new ChatValidationException(
                    $"unknown action '{action}' — use goto_definition|find_references|document_symbols|workspace_symbols|diagnostics");
        }
    }

    private string FileUri(JsonElement arguments, string workspace)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ChatValidationException("path is required for this action");
        }

        PathJailValidator.Validate(path, workspace, denylist);
        var full = Path.GetFullPath(Path.Join(workspace, path));
        return new Uri(full, UriKind.Absolute).AbsoluteUri;
    }

    /// <summary>DocumentSymbol[] (hierarchical, flattened) or SymbolInformation[].</summary>
    private static IReadOnlyList<object> NormalizeSymbols(JsonElement result)
    {
        var list = new List<object>();
        if (result.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in result.EnumerateArray())
        {
            FlattenSymbol(el, container: null, list);
            if (list.Count >= MaxResults)
            {
                break;
            }
        }
        return list;
    }

    private static void FlattenSymbol(JsonElement el, string? container, List<object> list)
    {
        if (list.Count >= MaxResults)
        {
            return;
        }

        var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
        var kind = el.TryGetProperty("kind", out var k) && k.TryGetInt32(out var ki) ? ki : 0;
        if (el.TryGetProperty("location", out var loc))
        {
            // SymbolInformation — flat shape.
            var (line, character) = StartOf(loc);
            var file = loc.TryGetProperty("uri", out var u) ? u.GetString() : null;
            list.Add(new { name, kind, file, line, character, container });
            return;
        }

        if (el.TryGetProperty("selectionRange", out var sel) || el.TryGetProperty("range", out sel))
        {
            var (line, character) = StartOf(sel);
            list.Add(new { name, kind, file = (string?)null, line, character, container });
        }

        if (el.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                FlattenSymbol(child, name, list);
            }
        }
    }

    /// <summary>Location | Location[] | LocationLink[] → uniform {file,line,character}.</summary>
    private static IReadOnlyList<object> NormalizeLocations(JsonElement result)
    {
        var list = new List<object>();
        IEnumerable<JsonElement> items = result.ValueKind == JsonValueKind.Array
            ? result.EnumerateArray()
            : [result];
        foreach (var el in items)
        {
            if (list.Count >= MaxResults)
            {
                break;
            }

            var file = el.TryGetProperty("uri", out var u) ? u.GetString()
                : el.TryGetProperty("targetUri", out var tu) ? tu.GetString() : null;
            var range = el.TryGetProperty("range", out var r) ? r
                : el.TryGetProperty("targetSelectionRange", out var tsr) ? tsr
                : default;
            var (line, character) = StartOf(range);
            list.Add(new { file, line, character });
        }
        return list;
    }

    /// <summary>SymbolInformation[] → {name,kind,file,line,containerName}.</summary>
    private static IReadOnlyList<object> NormalizeSymbolInformation(JsonElement result)
    {
        var list = new List<object>();
        if (result.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in result.EnumerateArray())
        {
            if (list.Count >= MaxResults)
            {
                break;
            }
            var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            var kind = el.TryGetProperty("kind", out var k) && k.TryGetInt32(out var ki) ? ki : 0;
            var container = el.TryGetProperty("containerName", out var cn) ? cn.GetString() : null;
            var file = el.TryGetProperty("location", out var loc) && loc.TryGetProperty("uri", out var u)
                ? u.GetString() : null;
            var (line, character) = el.TryGetProperty("location", out var loc2) ? StartOf(loc2) : (0, 0);
            list.Add(new { name, kind, file, line, character, container });
        }
        return list;
    }

    /// <summary>Pull-model diagnostic → {message,severity,line,character,code}.</summary>
    private static IReadOnlyList<object> NormalizeDiagnostics(JsonElement result)
    {
        var list = new List<object>();
        var source = result.TryGetProperty("items", out var it) ? it : result;
        if (source.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in source.EnumerateArray())
        {
            if (list.Count >= MaxResults)
            {
                break;
            }
            var message = el.TryGetProperty("message", out var m) ? m.GetString() : null;
            var severity = el.TryGetProperty("severity", out var s) && s.TryGetInt32(out var si) ? si : 0;
            var code = el.TryGetProperty("code", out var c)
                ? c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText() : null;
            var (line, character) = el.TryGetProperty("range", out var r) ? StartOf(r) : (0, 0);
            list.Add(new { message, severity, line, character, code });
        }
        return list;
    }

    private static (int Line, int Character) StartOf(JsonElement rangeOrLocation)
    {
        var range = rangeOrLocation.TryGetProperty("range", out var inner) ? inner : rangeOrLocation;
        if (range.TryGetProperty("start", out var start))
        {
            var line = start.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : 0;
            var ch = start.TryGetProperty("character", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            return (line, ch);
        }
        return (0, 0);
    }

    private static string ErrorJson(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
