namespace Taskboard.Application.Contracts.Operations;

/// <summary>
/// Bounded in-memory buffer of operation log lines (ring buffer, last
/// <see cref="DefaultCapacity"/> entries). Volatile by design — it explains
/// the most recent install/sync runs, it is not an audit trail.
/// </summary>
public class OperationLog
{
    public const int DefaultCapacity = 200;

    private readonly object _gate = new();
    private readonly Queue<OperationLogEntry> _entries;
    private readonly int _capacity;

    public OperationLog(int capacity = DefaultCapacity)
    {
        _capacity = capacity;
        _entries = new Queue<OperationLogEntry>(capacity);
    }

    public void Add(string level, string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new OperationLogEntry(DateTimeOffset.UtcNow, level, message));
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public void Info(string message) => Add("info", message);

    public void Warn(string message) => Add("warn", message);

    public void Error(string message) => Add("error", message);

    public IReadOnlyList<OperationLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }
}

/// <summary>Log lines produced by MCP provisioning runs.</summary>
public sealed class McpOperationLog : OperationLog
{
}

/// <summary>Log lines produced by skills install and sync runs (shared).</summary>
public sealed class SkillsOperationLog : OperationLog
{
}
