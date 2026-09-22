using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-board-cockpit-agent-observability RF-004: board runs are
/// pipelines — issue-scoped control/state must route to the execution bound
/// to the issue, with the legacy orchestrator kept as fallback.
/// </summary>
public class AgentControlServiceTests
{
    private const string IssueId = "issue-42";
    private const string RunId = "run-abc";

    private readonly IAgentOrchestrationService _orchestration = Substitute.For<IAgentOrchestrationService>();
    private readonly ISteerQueue _steer = Substitute.For<ISteerQueue>();
    private readonly IPipelineOrchestrator _pipelines = Substitute.For<IPipelineOrchestrator>();
    private readonly AgentControlService _sut;

    public AgentControlServiceTests()
    {
        var threadEvents = Substitute.For<IThreadEventStreamService>();
        var gate = new PermissionGate(threadEvents);
        var acpClient = new AcpSessionClient([], new AcpSessionOptions());
        var workspace = new WorkspaceService(null, Path.GetTempPath(), NullLogger<WorkspaceService>.Instance);
        var sessions = new AgentSessionManager(
            acpClient,
            Substitute.For<IAgentAcpClient>(),
            [],
            Substitute.For<IServiceScopeFactory>(),
            threadEvents,
            gate,
            workspace,
            NullLogger<AgentSessionManager>.Instance);
        var eventRepository = Substitute.For<IAgentRunEventRepository>();
        var sink = Substitute.For<IAgentExecutionEventSink>();
        sink.EmitAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<AgentExecutionEvent>(0)));

        _sut = new AgentControlService(
            _orchestration, _steer, _pipelines, sessions, gate, acpClient, eventRepository, sink);
    }

    private static PipelineExecutionDto Execucao(string status, params PipelineStageDto[] stages) =>
        new(RunId, "single-agent", "afonsoft/agent-harness", "main", status,
            "/tmp/wt", DateTime.UtcNow, null, stages, IssueId);

    [Fact]
    public async Task Dado_IssueComPipelineAtivo_Quando_Cancel_Entao_CancelaRunDoPipeline()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("Running"));

        var result = await _sut.ExecuteAsync(
            new AgentControlRequest(AgentEventScope.Issue, IssueId, "cancel"), CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Accepted);
        await _pipelines.Received(1).CancelAsync(RunId, Arg.Any<CancellationToken>());
        await _orchestration.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueSemPipeline_Quando_Cancel_Entao_UsaOrchestrationLegada()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns((PipelineExecutionDto?)null);
        _orchestration.CancelAsync(IssueId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ExecuteAsync(
            new AgentControlRequest(AgentEventScope.Issue, IssueId, "cancel"), CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Accepted);
        await _orchestration.Received(1).CancelAsync(IssueId, Arg.Any<CancellationToken>());
        await _pipelines.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueComPipelineConcluido_Quando_Cancel_Entao_UsaOrchestrationLegada()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("Completed"));
        _orchestration.CancelAsync(IssueId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ExecuteAsync(
            new AgentControlRequest(AgentEventScope.Issue, IssueId, "cancel"), CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Accepted);
        await _orchestration.Received(1).CancelAsync(IssueId, Arg.Any<CancellationToken>());
        await _pipelines.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueComPipelineAtivo_Quando_Steer_Entao_EnfileiraNoRun()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("Running"));

        var result = await _sut.ExecuteAsync(
            new AgentControlRequest(AgentEventScope.Issue, IssueId, "steer", Content: "use nullable refs"),
            CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Accepted);
        _steer.Received(1).Enqueue(RunId, "use nullable refs");
    }

    [Fact]
    public async Task Dado_IssueComPipelineFalho_Quando_Retry_Entao_ReexecutaStageFalho()
    {
        var failed = new PipelineStageDto(
            "build", "Build", "AgentWork", "Failed", null, null, 1, null, "exit 1", []);
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("AwaitingRetry", failed));
        _pipelines.RetryStageAsync(RunId, "build", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Execucao("Running"));

        var result = await _sut.ExecuteAsync(
            new AgentControlRequest(AgentEventScope.Issue, IssueId, "retry"), CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Ok);
        await _pipelines.Received(1).RetryStageAsync(RunId, "build", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueComPipelineAtivo_Quando_ReplyPermissaoDeStage_Entao_AprovaStageNoRun()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("WaitingApproval"));
        _pipelines.ApproveStageAsync(RunId, "approve-plan", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Execucao("Running"));

        var result = await _sut.ReplyPermissionAsync(
            new AgentPermissionReplyRequest(AgentEventScope.Issue, IssueId, "stage:approve-plan", "allow"),
            CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Ok);
        await _pipelines.Received(1).ApproveStageAsync(RunId, "approve-plan", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueAguardandoAprovacao_Quando_GetScopeState_Entao_RetornaWaitingPermission()
    {
        _pipelines.GetLatestByIssueAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Execucao("WaitingApproval"));

        var result = await _sut.GetScopeStateAsync(AgentEventScope.Issue, IssueId, CancellationToken.None);

        result.Status.ShouldBe(AgentControlStatus.Ok);
        var state = result.Payload.ShouldBeOfType<AgentScopeState>();
        state.State.ShouldBe("waiting_permission");
    }
}
