namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-001: canonical string values for the
/// quota window kind, sample source and confidence — strings keep the JSON
/// wire shape stable without a enum serialization contract.
/// </summary>
public static class ProviderWindowKinds
{
    public const string FiveHour = "fiveHour";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Rate = "rate";
    /// <summary>RF-010 marker for providers without quota — never persisted.</summary>
    public const string Na = "n/a";
}

public static class ProviderUsageSources
{
    public const string Api = "api";
    public const string Cli = "cli";
    public const string LocalEstimate = "local_estimate";
}

public static class ProviderUsageConfidences
{
    public const string Measured = "measured";
    public const string Estimated = "estimated";
}
