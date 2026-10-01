using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.Repositories;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// B-13: threads criadas como "New conversation" derivam o título do primeiro
/// prompt enfileirado no caminho agent (<c>EnqueuePromptAsync</c>).
/// </summary>
public sealed class AgentSessionManagerEnqueueTests : IDisposable
{
    private readonly string _dbPath = Path.Join(Path.GetTempPath(), $"tb-enqueue-{Guid.NewGuid()}.sqlite");
    private readonly TaskboardDbContext _ctx;
    private readonly AgentSessionManager _sut;

    public AgentSessionManagerEnqueueTests()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _ctx = new TaskboardDbContext(options);
        _ctx.Database.EnsureCreated();

        var threadRepo = new EfCoreRepository<AiChatThread>(_ctx);
        var eventRepo = new EfCoreRepository<AiChatEvent>(_ctx);

        var services = new ServiceCollection();
        services.AddScoped<IRepository<AiChatThread>>(_ => threadRepo);
        services.AddScoped<IRepository<AiChatEvent>>(_ => eventRepo);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _sut = new AgentSessionManager(
            new AcpSessionClient([], new AcpSessionOptions()),
            Substitute.For<IAgentAcpClient>(),
            [],
            scopeFactory,
            Substitute.For<IThreadEventStreamService>(),
            new PermissionGate(Substitute.For<IThreadEventStreamService>()),
            new WorkspaceService(null, Path.GetTempPath(), NullLogger<WorkspaceService>.Instance),
            NullLogger<AgentSessionManager>.Instance);
    }

    [Fact]
    public async Task Dado_TituloGenerico_Quando_EnqueuePrompt_Entao_DerivaTitulo()
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "New conversation", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, AgentType.Claude, workspacePath: "/work");
        _ctx.Set<AiChatThread>().Add(thread);
        await _ctx.SaveChangesAsync();

        var dto = await _sut.EnqueuePromptAsync(thread.Id.Value, "rename the settings tabs");

        dto.ShouldNotBeNull();
        _ctx.Set<AiChatThread>().Local.Single(t => t.Id == thread.Id)
            .Title.ShouldBe("rename the settings tabs");
    }

    [Fact]
    public async Task Dado_TituloCustomizado_Quando_EnqueuePrompt_Entao_PreservaTitulo()
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "my custom title", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, AgentType.Claude, workspacePath: "/work");
        _ctx.Set<AiChatThread>().Add(thread);
        await _ctx.SaveChangesAsync();

        var dto = await _sut.EnqueuePromptAsync(thread.Id.Value, "rename the settings tabs");

        dto.ShouldNotBeNull();
        _ctx.Set<AiChatThread>().Local.Single(t => t.Id == thread.Id)
            .Title.ShouldBe("my custom title");
    }

    public void Dispose()
    {
        _ctx.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
