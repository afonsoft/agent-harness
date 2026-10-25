using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Application.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Diagnostics;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Delegation;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Diagnostics;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-T9: diagnostics card for
/// failed/cancelled ChatRun and DelegationTask — error class, tool failures,
/// last status events, pressure/compaction stats.
/// </summary>
public class DiagnosticsCardTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"diag-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public void Dado_ErroNulo_Quando_StatusCancelado_Entao_ClasseCancelled()
    {
        RunDiagnosticsClassifier.ClassifyError("stopped", null).ShouldBe("cancelled");
        RunDiagnosticsClassifier.ClassifyError("cancelled", null).ShouldBe("cancelled");
        RunDiagnosticsClassifier.ClassifyError("failed", null).ShouldBe("unknown");
    }

    [Fact]
    public void Dado_ClassesDeErro_Quando_Classifica_Entao_BucketCorreto()
    {
        RunDiagnosticsClassifier.ClassifyError("failed", "credential-path: ssh-dir").ShouldBe("policy-refusal");
        RunDiagnosticsClassifier.ClassifyError("failed", "run cancelled by user").ShouldBe("cancelled");
        RunDiagnosticsClassifier.ClassifyError("failed", "provider request timed out").ShouldBe("timeout");
        RunDiagnosticsClassifier.ClassifyError("failed", "preflight blocked: no cli").ShouldBe("preflight");
        RunDiagnosticsClassifier.ClassifyError("failed", "provider 429 rate limit").ShouldBe("provider");
        RunDiagnosticsClassifier.ClassifyError("failed", "null ref inside parser").ShouldBe("internal");
    }

    [Fact]
    public void Dado_RunCompleted_Quando_BuildCard_Entao_SemDiagnostics()
    {
        var run = ChatRun.Create(ChatRunId.NewGuid(), ChatConversationId.NewGuid(), ChatMessageId.NewGuid());
        run.Start();
        run.Complete(null, null);

        ChatRunDiagnostics.ForRun(run, []).ShouldBeNull();
    }

    [Fact]
    public void Dado_RunFailedComToolRecusada_Quando_BuildCard_Entao_CardCompleto()
    {
        var convId = ChatConversationId.NewGuid();
        var run = ChatRun.Create(ChatRunId.NewGuid(), convId, ChatMessageId.NewGuid());
        run.Start();
        run.RecordContextStats(128_000, 2);
        run.Fail("provider 429 rate limit");

        var toolMessages = new List<ChatMessage>
        {
            ChatMessage.CreateTool(convId, "c1", "write_file",
                """{"refused":true,"reason":"credential-path: ssh-dir"}""", refused: true),
            ChatMessage.CreateTool(convId, "c2", "echo_tool", """{"output":"fine"}"""),
            ChatMessage.CreateTool(convId, "c3", "shell_exec", """{"error":"exit 127"}"""),
        };

        var card = ChatRunDiagnostics.ForRun(run, toolMessages).ShouldNotBeNull();
        card.ErrorClass.ShouldBe("provider");
        card.ToolFailures.Select(f => f.Name).ShouldBe(["write_file", "shell_exec"]);
        card.ToolFailures.First(f => f.Name == "write_file").Reason.ShouldContain("credential-path");
        card.ToolFailures.ShouldNotContain(f => f.Name == "echo_tool");
        card.CompactionCount.ShouldBe(2);
        card.ContextTokensLimit.ShouldBe(128_000);
        card.LastEvents.ShouldContain(e => e.StartsWith("started"));
        card.LastEvents.ShouldContain(e => e.StartsWith("compaction x2"));
        card.LastEvents.ShouldContain(e => e.Contains("status=failed"));
    }

    [Fact]
    public async Task Dado_TaskFailed_Quando_GetAsync_Entao_DtoCarregaCard()
    {
        await using var context = NewContext();
        var repo = new EfCoreDelegationTaskRepository(context);
        var dto = await repo.AddAsync(new CreateDelegationTaskRequest(
            "falhar", "codex", "conv-diag", "/tmp/ws"));
        dto.Diagnostics.ShouldBeNull();

        var terminal = await repo.FinalizeAsync(
            dto.Id, DelegationTaskStatus.Failed, "exit code 1", DateTime.UtcNow);
        var card = terminal.ShouldNotBeNull().Diagnostics.ShouldNotBeNull();
        card.ErrorClass.ShouldBe("internal");
        card.ToolFailures.ShouldBeEmpty();
        card.LastEvents.ShouldContain(e => e.Contains("status=failed"));
        card.LastEvents.ShouldContain(e => e.Contains("error:"));
    }

    private TaskboardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        var context = new TaskboardDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = _dbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
