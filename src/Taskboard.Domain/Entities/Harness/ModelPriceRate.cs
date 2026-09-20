namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Configurable pricing row — USD per 1M tokens, keyed by model name or prefix
/// (SPEC-20260919-ade-observability-finops RF-002). `ModelPattern = "*"` is the
/// fallback rate applied when no specific pattern matches.
/// </summary>
public sealed class ModelPriceRate : Entity<Guid>
{
    /// <summary>Provider label (anthropic, openai, deepseek, google, ...).</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// Model name or prefix to match (e.g. `claude-sonnet-4`, `gpt-5`). Matching
    /// prefers the longest pattern; `"*"` is the wildcard fallback.
    /// </summary>
    public string ModelPattern { get; private set; } = string.Empty;

    public decimal InputPer1M { get; private set; }

    public decimal OutputPer1M { get; private set; }

    public decimal CacheWritePer1M { get; private set; }

    public decimal CacheReadPer1M { get; private set; }

    private ModelPriceRate()
    {
    }

    public ModelPriceRate(
        Guid id,
        string provider,
        string modelPattern,
        decimal inputPer1M,
        decimal outputPer1M,
        decimal cacheWritePer1M,
        decimal cacheReadPer1M)
        : base(id)
    {
        Provider = provider;
        ModelPattern = modelPattern;
        SetPrices(inputPer1M, outputPer1M, cacheWritePer1M, cacheReadPer1M);
    }

    public void SetPrices(decimal inputPer1M, decimal outputPer1M, decimal cacheWritePer1M, decimal cacheReadPer1M)
    {
        if (inputPer1M < 0 || outputPer1M < 0 || cacheWritePer1M < 0 || cacheReadPer1M < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputPer1M), "Prices cannot be negative.");
        }

        InputPer1M = inputPer1M;
        OutputPer1M = outputPer1M;
        CacheWritePer1M = cacheWritePer1M;
        CacheReadPer1M = cacheReadPer1M;
    }
}
