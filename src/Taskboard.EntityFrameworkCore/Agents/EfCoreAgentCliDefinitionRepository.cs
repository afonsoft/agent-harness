using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>
/// EF Core store for user-declared agent CLIs — entity↔DTO boundary lives
/// here so Contracts never leak Domain types
/// (SPEC-20260928-ai-code-generic-cli RF-002).
/// </summary>
public sealed class EfCoreAgentCliDefinitionRepository : IAgentCliDefinitionRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentCliDefinitionRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AgentCliDefinitionDto>> ListAsync(CancellationToken ct = default)
    {
        var entities = await _context.AgentCliDefinitions
            .OrderBy(d => d.DisplayName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return entities.Select(ToDto).ToList();
    }

    public async Task<AgentCliDefinitionDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _context.AgentCliDefinitions.FindAsync([id], ct).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public Task<bool> DisplayNameExistsAsync(string displayName, CancellationToken ct = default) =>
        _context.AgentCliDefinitions
            .AnyAsync(d => d.DisplayName == displayName.Trim(), ct);

    public async Task<AgentCliDefinitionDto> AddAsync(
        UpsertAgentCliDefinitionRequest request, CancellationToken ct = default)
    {
        var entity = AgentCliDefinition.Create(
            request.DisplayName,
            request.Executable,
            request.ArgsTemplate ?? string.Empty,
            request.Transport ?? "pty",
            request.ModelFlag,
            request.VersionArgs,
            request.Enabled,
            promptDelivery: request.PromptDelivery,
            modelListArgs: request.ModelListArgs);

        _context.AgentCliDefinitions.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<AgentCliDefinitionDto?> UpdateAsync(
        string id, UpsertAgentCliDefinitionRequest request, CancellationToken ct = default)
    {
        var entity = await _context.AgentCliDefinitions.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.Update(
            request.DisplayName,
            request.Executable,
            request.ArgsTemplate ?? string.Empty,
            request.Transport ?? entity.Transport,
            request.ModelFlag,
            request.VersionArgs,
            request.Enabled,
            promptDelivery: request.PromptDelivery,
            modelListArgs: request.ModelListArgs);

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await _context.AgentCliDefinitions.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        _context.AgentCliDefinitions.Remove(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static AgentCliDefinitionDto ToDto(AgentCliDefinition d) =>
        new(d.Id, d.DisplayName, d.Executable, d.ArgsTemplate, d.Transport,
            d.ModelFlag, d.VersionArgs, d.Enabled, Resolved: false,
            d.PromptDelivery, d.ModelListArgs);
}
