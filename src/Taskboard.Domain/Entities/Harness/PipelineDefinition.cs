namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Pipeline template — an ordered DAG of stages
/// (SPEC-20260919-ade-multi-agent-orchestration RF-001/RF-002).
/// </summary>
public sealed record PipelineDefinition(
    string TemplateId,
    string Name,
    IReadOnlyList<PipelineStage> Stages)
{
    /// <summary>
    /// Validates the DAG: unique stage keys, resolvable dependencies, acyclic.
    /// Throws <see cref="DomainException"/> with
    /// <see cref="TaskboardDomainErrorCodes.InvalidPipelineDag"/> on violation.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TemplateId) || Stages.Count == 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineDag, "Pipeline must have an id and at least one stage.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in Stages)
        {
            if (!keys.Add(stage.Key))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidPipelineDag, $"Duplicate stage key '{stage.Key}'.");
            }
        }

        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var stage in Stages)
        {
            indegree[stage.Key] = stage.DependsOn.Count;
            dependents[stage.Key] = [];
            foreach (var dep in stage.DependsOn)
            {
                if (!keys.Contains(dep))
                {
                    throw new DomainException(
                        TaskboardDomainErrorCodes.InvalidPipelineDag,
                        $"Stage '{stage.Key}' depends on unknown stage '{dep}'.");
                }
            }
        }

        foreach (var stage in Stages)
        {
            foreach (var dep in stage.DependsOn)
            {
                dependents[dep].Add(stage.Key);
            }
        }

        // Kahn's algorithm — any leftover indegree means a cycle.
        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var next in dependents[current])
            {
                if (--indegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (visited != Stages.Count)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineDag, "Pipeline DAG contains a cycle.");
        }
    }
}
