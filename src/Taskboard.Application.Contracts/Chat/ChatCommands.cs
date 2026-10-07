namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261017-chat-polish RF-003/RF-004: one ⌘K-palette row. Execution is
/// resolved by <see cref="Id"/> on the host — the registry stays data-driven
/// and unit-testable (Enabled == canExecute(state)).
/// </summary>
/// <param name="Id">Stable command id (e.g. "chat.new", "ws.tab.terminal").</param>
/// <param name="LabelKey">UiStrings key for the label.</param>
/// <param name="Keywords">Extra lowercase search terms (locale-agnostic).</param>
/// <param name="Group">"session" | "workspace" | "app" — palette section order.</param>
/// <param name="Enabled">canExecute(state) — disabled rows render dimmed and do not fire.</param>
public sealed record ChatCommandItem(
    string Id, string LabelKey, string Keywords, string Group, bool Enabled = true);

/// <summary>The conversation/runtime flags the command gates read.</summary>
public sealed record ChatCommandState(
    bool HasConversation,
    bool RunActive,
    bool RunPaused,
    bool Archived);

/// <summary>
/// Builds the static command list for a state. Pure — the Blazor host maps
/// <see cref="ChatCommandItem.Id"/> to actions; the ids are the contract.
/// </summary>
public static class ChatCommands
{
    /// <summary>Workspace-tab command ids in panel order.</summary>
    public static readonly IReadOnlyList<string> WorkspaceTabs =
        ["tasks", "changes", "terminal", "editor", "plan", "preview", "browser"];

    /// <summary>App-page command ids in nav order.</summary>
    public static readonly IReadOnlyList<(string Id, string Route)> AppPages =
    [
        ("app.terminal", "/terminal"),
        ("app.editor", "/editor"),
        ("app.agents", "/agents"),
        ("app.jobs", "/jobs"),
        ("app.settings", "/settings"),
    ];

    public static IReadOnlyList<ChatCommandItem> Build(ChatCommandState state)
    {
        var items = new List<ChatCommandItem>
        {
            new("chat.new", "chat.cmd.new", "new session conversa nova", "session"),
            new("chat.archive", "chat.cmd.archive", "archive arquivar", "session",
                Enabled: state.HasConversation && !state.Archived),
            new("chat.done", "chat.cmd.done", "done concluir finalizar archive", "session",
                Enabled: state.HasConversation && !state.Archived),
            new("chat.run.stop", "chat.cmd.stop", "stop parar cancelar", "session",
                Enabled: state.HasConversation && state.RunActive),
            new("chat.run.pause", "chat.cmd.pause", "pause pausar resume continuar", "session",
                Enabled: state.HasConversation && state.RunActive),
        };

        foreach (var tab in WorkspaceTabs)
        {
            items.Add(new ChatCommandItem(
                $"ws.tab.{tab}", $"chat.cmd.tab.{tab}", $"tab {tab} abrir", "workspace",
                Enabled: state.HasConversation));
        }

        foreach (var (id, _) in AppPages)
        {
            items.Add(new ChatCommandItem(id, $"chat.cmd.{id[4..]}-page", "goto ir pagina", "app"));
        }

        items.Add(new ChatCommandItem("app.sidebar", "chat.cmd.sidebar", "menu navegacao", "app"));
        return items;
    }
}

/// <summary>⌘K palette fuzzy match — the ordering the visible list shares
/// with the keyboard selection (Enter fires Filtered[ActiveIndex]).</summary>
public static class ChatCommandPalette
{
    /// <summary>
    /// Substring match on label+keywords (all query words must hit), ranked:
    /// label starts-with &gt; label contains &gt; keywords-only, then group
    /// order (session → workspace → app), capped at 12.
    /// </summary>
    public static IReadOnlyList<ChatCommandItem> Filter(
        IReadOnlyList<ChatCommandItem> items, Func<string, string> labelOf, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items
            .Select(item => (Item: item, Label: labelOf(item.LabelKey)))
            .Where(e => words.All(w =>
                e.Label.Contains(w, StringComparison.OrdinalIgnoreCase)
                || e.Item.Keywords.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => words.Any(w => e.Label.StartsWith(w, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .ThenBy(e => words.Any(w => e.Label.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .ThenBy(e => e.Item.Group switch { "session" => 0, "workspace" => 1, _ => 2 })
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(e => e.Item)
            .ToList();
    }
}
