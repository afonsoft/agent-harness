namespace Taskboard.ValueObjects;

/// <summary>Identifier of an agent automation (SPEC-20261021-agent-automations).</summary>
public sealed record AgentAutomationId : StringIdBase
{
    public AgentAutomationId(string value)
        : base(value)
    {
    }

    public static AgentAutomationId From(string value) => new(value);

    public static AgentAutomationId NewGuid() => new(Guid.NewGuid().ToString("N"));
}

/// <summary>Identifier of one fired automation run.</summary>
public sealed record AgentAutomationRunId : StringIdBase
{
    public AgentAutomationRunId(string value)
        : base(value)
    {
    }

    public static AgentAutomationRunId From(string value) => new(value);

    public static AgentAutomationRunId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
