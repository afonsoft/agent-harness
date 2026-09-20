namespace Taskboard.Specs;

/// <summary>A non-fatal parsing anomaly on a spec file (RF-001 rule).</summary>
public sealed record SpecLintWarning(string Code, string Message);
