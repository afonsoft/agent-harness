using System;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentRunTests
{
    [Fact]
    public void Dado_NovoRun_Quando_Criar_Entao_IniciaComoQueued()
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Claude, DateTimeOffset.UtcNow);

        run.State.ShouldBe(AgentRunState.Queued);
        run.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public void Dado_RunQueued_Quando_MarkRunning_Entao_MudaParaRunning()
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Claude, DateTimeOffset.UtcNow);

        run.MarkRunning();

        run.State.ShouldBe(AgentRunState.Running);
    }

    [Fact]
    public void Dado_RunFinalizado_Quando_MarkRunning_Entao_NaoVoltaParaRunning()
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Claude, DateTimeOffset.UtcNow);
        run.MarkFinished(AgentRunState.Succeeded, DateTimeOffset.UtcNow);

        run.MarkRunning();

        run.State.ShouldBe(AgentRunState.Succeeded);
    }

    [Fact]
    public void Dado_EstadoNaoFinal_Quando_MarkFinished_Entao_LancaExcecao()
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Claude, DateTimeOffset.UtcNow);

        Should.Throw<ArgumentException>(() => run.MarkFinished(AgentRunState.Running, DateTimeOffset.UtcNow));
        Should.Throw<ArgumentException>(() => run.MarkFinished(AgentRunState.Queued, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Dado_RunEmAndamento_Quando_MarkFinished_Entao_DefineEstadoETimestamp()
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Codex, DateTimeOffset.UtcNow);
        run.MarkRunning();
        var finishedAt = DateTimeOffset.UtcNow;

        run.MarkFinished(AgentRunState.Canceled, finishedAt);

        run.State.ShouldBe(AgentRunState.Canceled);
        run.FinishedAt.ShouldBe(finishedAt);
    }
}
