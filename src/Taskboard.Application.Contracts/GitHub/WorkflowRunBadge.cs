namespace Taskboard.GitHub;

/// <summary>
/// Mapeia (status, conclusion) de um run para a apresentação do badge na tela
/// Workflow (RF-004 do SPEC-20260918-workflow-github-actions). Função pura —
/// sem dependência de Blazor para ser testável em unit tests.
/// </summary>
public static class WorkflowRunBadge
{
    /// <summary>Classes CSS do badge (Bootstrap + animação quando vivo).</summary>
    /// <param name="status">Status do run (<c>in_progress</c>, <c>queued</c>, <c>completed</c>…).</param>
    /// <param name="conclusion">Conclusão quando <paramref name="status"/> é <c>completed</c>.</param>
    public static string CssClass(string? status, string? conclusion)
    {
        if (IsLive(status))
        {
            return "badge text-bg-warning workflow-badge-live";
        }

        return Normalize(conclusion) switch
        {
            "success" => "badge text-bg-success",
            "failure" or "timed_out" or "startup_failure" or "action_required" => "badge text-bg-danger",
            _ => "badge text-bg-secondary",
        };
    }

    /// <summary>Texto exibido no badge.</summary>
    public static string Label(string? status, string? conclusion)
    {
        if (IsLive(status))
        {
            return Normalize(status)!;
        }

        return Normalize(conclusion) ?? Normalize(status) ?? "unknown";
    }

    /// <summary>True quando o run ainda está vivo (dirige o auto-refresh de 60s).</summary>
    public static bool IsLive(string? status) =>
        Normalize(status) is "in_progress" or "queued" or "requested" or "waiting" or "pending";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
