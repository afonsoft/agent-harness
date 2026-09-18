using System.Text.Json;
using Taskboard.Agents;
using Task = System.Threading.Tasks.Task;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Agents;

/// <summary>
/// Per-CLI model tier overrides stored as JSON in <c>ConfigurationOverrides</c>
/// under <c>Taskboard:Agents:Models:{AgentType}</c>
/// (SPEC-20260918-agent-model-config). Unset slots fall back to the curated
/// <see cref="AgentCliModels"/> table per tier.
/// </summary>
public sealed class AgentModelConfigService : IAgentModelConfigService
{
    public const int MaxModelNameLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ConfigurationOverride> _overrides;

    public AgentModelConfigService(IRepository<ConfigurationOverride> overrides)
    {
        _overrides = overrides;
    }

    public static string KeyFor(AgentType agentType) => $"Taskboard:Agents:Models:{agentType}";

    public async Task<AgentModelConfigDto> GetConfigAsync(AgentType agentType, CancellationToken cancellationToken = default)
    {
        var saved = await LoadOverrideAsync(agentType, cancellationToken);
        var defaults = new AgentModelTierSet(
            AgentCliModels.ModelFor(agentType, AgentModelTier.Lite),
            AgentCliModels.ModelFor(agentType, AgentModelTier.Normal),
            AgentCliModels.ModelFor(agentType, AgentModelTier.Ultra));

        return new AgentModelConfigDto(
            agentType,
            AgentCliModels.SupportsModelSelection(agentType),
            saved is null ? "default" : "override",
            saved?.Lite ?? defaults.Lite,
            saved?.Normal ?? defaults.Normal,
            saved?.Ultra ?? defaults.Ultra,
            defaults,
            AgentCliModels.Catalog(agentType));
    }

    public async Task SetConfigAsync(
        AgentType agentType, SaveAgentModelConfigRequest request, CancellationToken cancellationToken = default)
    {
        if (!AgentCliModels.SupportsModelSelection(agentType))
        {
            throw new ArgumentException($"Agent '{agentType}' has no headless model flag.", nameof(agentType));
        }

        var set = new AgentModelTierSet(
            Normalize(request.Lite, nameof(request.Lite)),
            Normalize(request.Normal, nameof(request.Normal)),
            Normalize(request.Ultra, nameof(request.Ultra)));

        var existing = await FindAsync(agentType, cancellationToken);
        var json = JsonSerializer.Serialize(set, JsonOptions);
        if (existing is null)
        {
            await _overrides.AddAsync(
                new ConfigurationOverride(Guid.NewGuid())
                {
                    Key = KeyFor(agentType),
                    Value = json,
                    UpdatedAt = DateTime.UtcNow,
                },
                cancellationToken);
        }
        else
        {
            existing.Value = json;
            existing.UpdatedAt = DateTime.UtcNow;
            await _overrides.UpdateAsync(existing, cancellationToken);
        }

        await _overrides.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteConfigAsync(AgentType agentType, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(agentType, cancellationToken);
        if (existing is not null)
        {
            await _overrides.DeleteAsync(existing, cancellationToken);
            await _overrides.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<string?> ResolveModelAsync(
        AgentType agentType, AgentModelTier tier, CancellationToken cancellationToken = default)
    {
        var saved = await LoadOverrideAsync(agentType, cancellationToken);
        return saved?.For(tier) ?? AgentCliModels.ModelFor(agentType, tier);
    }

    private async Task<AgentModelTierSet?> LoadOverrideAsync(AgentType agentType, CancellationToken cancellationToken)
    {
        var row = await FindAsync(agentType, cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentModelTierSet>(row.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ConfigurationOverride?> FindAsync(AgentType agentType, CancellationToken cancellationToken)
    {
        var key = KeyFor(agentType);
        var all = await _overrides.ListAsync(cancellationToken);
        return all.FirstOrDefault(o => o.Key == key);
    }

    private static string? Normalize(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxModelNameLength)
        {
            throw new ArgumentException(
                $"Model name must be at most {MaxModelNameLength} characters.", parameterName);
        }

        return trimmed;
    }
}
