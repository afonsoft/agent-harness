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

        var keys = CollectUniqueKeys();
        var (indegree, dependents) = BuildGraph(keys);
        AssertAcyclic(indegree, dependents);
    }

    private HashSet<string> CollectUniqueKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var duplicate = Stages.Select(stage => stage.Key)
            .FirstOrDefault(key => !keys.Add(key));
        if (duplicate is not null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineDag, $"Duplicate stage key '{duplicate}'.");
        }

        return keys;
    }

    private (Dictionary<string, int> Indegree, Dictionary<string, List<string>> Dependents) BuildGraph(
        HashSet<string> keys)
    {
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var stage in Stages)
        {
            indegree[stage.Key] = stage.DependsOn.Count;
            dependents[stage.Key] = [];
            var unknown = stage.DependsOn.FirstOrDefault(dep => !keys.Contains(dep));
            if (unknown is not null)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidPipelineDag,
                    $"Stage '{stage.Key}' depends on unknown stage '{unknown}'.");
            }
        }

        foreach (var stage in Stages)
        {
            foreach (var dep in stage.DependsOn)
            {
                dependents[dep].Add(stage.Key);
            }
        }

        return (indegree, dependents);
    }

    private void AssertAcyclic(
        Dictionary<string, int> indegree,
        Dictionary<string, List<string>> dependents)
    {
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
