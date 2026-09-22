namespace Taskboard.Harness;

/// <summary>
/// Pricing constants for FinOps cost projection.
/// SPEC-20260922-finops-dashboard-detail RF-007.
/// </summary>
public static class FinOpsPricing
{
    /// <summary>
    /// Flat USD per 1M tokens applied to CLI usage whose model has no
    /// <c>ModelPriceRate</c> match (reference session-monitor USD_PER_MTOKEN).
    /// Costs produced via this fallback are flagged as estimated in the UI.
    /// </summary>
    public const decimal FallbackUsdPerMTok = 9.5m;
}
