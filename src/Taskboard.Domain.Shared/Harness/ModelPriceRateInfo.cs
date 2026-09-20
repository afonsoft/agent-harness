namespace Taskboard.Harness;

/// <summary>
/// Pricing row consumed by <see cref="TokenCostCalculator"/> — USD per 1M
/// tokens (SPEC-20260919-ade-observability-finops RF-002).
/// `ModelPattern = "*"` is the global fallback rate.
/// </summary>
public sealed record ModelPriceRateInfo(
    string Provider,
    string ModelPattern,
    decimal InputPer1M,
    decimal OutputPer1M,
    decimal CacheWritePer1M,
    decimal CacheReadPer1M);
