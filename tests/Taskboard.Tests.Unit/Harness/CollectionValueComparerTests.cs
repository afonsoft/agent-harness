using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260928-ef-value-comparers RF-001/RF-002/RF-003: coleções persistidas
/// via <see cref="ReadOnlyListStringJsonValueConverter"/> devem declarar
/// <see cref="ListStringValueComparer"/> para que o EF Core compare por conteúdo
/// (sem warning de modelo e sem falsos dirty em reatribuição equivalente).
/// </summary>
public class CollectionValueComparerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;

    public CollectionValueComparerTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-comparer-{Guid.NewGuid()}.sqlite");
        _options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        using var context = new TaskboardDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Dado_Comparer_Quando_ColecoesEquivalentes_Entao_EqualsTrue()
    {
        var comparer = new ListStringValueComparer();
        comparer.EqualsExpression.Compile()(["a", "b"], new List<string> { "a", "b" }).ShouldBeTrue();
        comparer.EqualsExpression.Compile()(["a"], Array.Empty<string>()).ShouldBeFalse();
        comparer.EqualsExpression.Compile()(null!, null!).ShouldBeTrue();
    }

    [Fact]
    public void Dado_Comparer_Quando_Snapshot_Entao_CopiaIndependente()
    {
        var comparer = new ListStringValueComparer();
        var original = new List<string> { "x" };
        var snapshot = (List<string>)comparer.SnapshotExpression.Compile()(original);
        original.Add("y");
        snapshot.ShouldBe(["x"]);
    }

    [Fact]
    public void Dado_Modelo_Quando_InspecionaPropriedades_Entao_TemValueComparer()
    {
        using var context = new TaskboardDbContext(_options);

        var tags = context.Model
            .FindEntityType(typeof(ProjectMemoryItem))!
            .FindProperty(nameof(ProjectMemoryItem.Tags))!;
        var dependsOn = context.Model
            .FindEntityType(typeof(PipelineStageExecution))!
            .FindProperty(nameof(PipelineStageExecution.DependsOn))!;
        var triedAgents = context.Model
            .FindEntityType(typeof(PipelineStageExecution))!
            .FindProperty(nameof(PipelineStageExecution.TriedAgents))!;

        tags.GetValueComparer().ShouldNotBeNull("Tags deve declarar ValueComparer (RF-002)");
        dependsOn.GetValueComparer().ShouldNotBeNull("DependsOn deve declarar ValueComparer (RF-002)");
        triedAgents.GetValueComparer().ShouldNotBeNull("TriedAgents deve declarar ValueComparer (RF-002)");
    }

    [Fact]
    public void Dado_ItemTracked_Quando_TagsEquivalentes_Entao_NaoMarcaModified()
    {
        using var context = new TaskboardDbContext(_options);
        var item = ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            "afonsoft/agent-harness",
            "topic",
            "content",
            MemoryType.Fact,
            ["a", "b"]);
        context.ProjectMemoryItems.Add(item);
        context.SaveChanges();

        var entry = context.Entry(item);
        var property = entry.Property(i => i.Tags);
        property.IsModified = false;
        property.CurrentValue = new List<string> { "a", "b" };
        context.ChangeTracker.DetectChanges();

        property.IsModified.ShouldBeFalse(
            "reatribuição com conteúdo equivalente não deve sujar a entidade (RF-003)");
    }

    [Fact]
    public void Dado_ItemTracked_Quando_TagsMutadas_Entao_PersisteNovoValor()
    {
        ProjectMemoryItemId id;
        using (var context = new TaskboardDbContext(_options))
        {
            var item = ProjectMemoryItem.Create(
                ProjectMemoryItemId.NewGuid(),
                "afonsoft/agent-harness",
                "topic",
                "content",
                MemoryType.Fact,
                ["a"]);
            id = item.Id;
            context.ProjectMemoryItems.Add(item);
            context.SaveChanges();
        }

        using (var context = new TaskboardDbContext(_options))
        {
            var item = context.ProjectMemoryItems.Single(i => i.Id == id);
            item.UpdateContent("content", ["b", "c"]);
            context.SaveChanges();
        }

        using (var context = new TaskboardDbContext(_options))
        {
            var reloaded = context.ProjectMemoryItems.Single(i => i.Id == id);
            reloaded.Tags.ShouldBe(["b", "c"]);
        }
    }
}
