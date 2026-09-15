using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.GitHub;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentOrchestrationServiceTests
{
    [Fact]
    public async Task Dado_UmaRequisicao_Quando_Enfileirar_Entao_RegistraETransmiteLogDoSistema()
    {
        var logBroadcaster = Substitute.For<IAgentLogBroadcaster>();
        var service = CriarService(logBroadcaster: logBroadcaster);
        var request = CriarRequest();

        await service.EnqueueAsync(request);

        var logs = await service.GetLogsAsync(request.IssueId);
        logs.ShouldContain(log =>
            log.Stream == AgentLogStream.System &&
            log.Content.Contains($"Queued {request.AgentType}"));
        await logBroadcaster.Received(1).BroadcastAsync(
            Arg.Is<AgentLogMessage>(log => log.IssueId == request.IssueId && log.Stream == AgentLogStream.System),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_UmaRequisicao_Quando_Enfileirar_Entao_PersisteLogNoRepositorio()
    {
        var logRepository = Substitute.For<IAgentLogRepository>();
        var service = CriarService(agentLogRepository: logRepository);
        var request = CriarRequest();

        await service.EnqueueAsync(request);

        await AguardarAsync(async () =>
            logRepository.ReceivedCalls().Any(call =>
                call.GetMethodInfo().Name == nameof(IAgentLogRepository.AppendAsync)));

        await logRepository.Received(1).AppendAsync(
            Arg.Is<AgentLogMessage>(log => log.IssueId == request.IssueId && log.Stream == AgentLogStream.System),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_HistoricoPersistido_Quando_SemExecucaoEmMemoria_Entao_RetornaDoRepositorio()
    {
        var issueId = "issue-persisted";
        var expected = new AgentLogMessage(
            DateTimeOffset.UtcNow,
            issueId,
            AgentLogStream.System,
            "persisted log");
        var logRepository = Substitute.For<IAgentLogRepository>();
        logRepository.GetByIssueIdAsync(issueId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentLogMessage>>([expected]));
        var service = CriarService(agentLogRepository: logRepository);

        var logs = await service.GetLogsAsync(issueId);

        logs.ShouldHaveSingleItem().ShouldBe(expected);
    }

    [Fact]
    public async Task Dado_AgentesDescobertos_Quando_ConsultarDisponibilidade_Entao_MapeiaResultadoSemAlterarStatus()
    {
        var discoveryService = Substitute.For<IAgentDiscoveryService>();
        var expected = new AgentInfo("claude", "/usr/bin/claude", AgentType.Claude, AgentStatus.Available, "claude 1.2.3", null);
        discoveryService.DiscoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentInfo>>([expected]));
        var service = CriarService(discoveryService: discoveryService);

        var agents = await service.GetAvailableAgentsAsync();

        agents.ShouldHaveSingleItem().ShouldBe(expected);
    }

    [Fact]
    public async Task Dado_ExecucaoBemSucedida_Quando_ProcessarFila_Entao_MoveIssueParaReview()
    {
        var acpClient = Substitute.For<IAgentAcpClient>();
        acpClient.ExecuteAsync(
                Arg.Any<AgentExecutionRequest>(),
                Arg.Any<IProgress<AgentLogMessage>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentExecutionResult(0, true)));
        var gitHubService = Substitute.For<IGitHubService>();
        gitHubService.UpdateIssueColumnAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<GitHubBoardColumn?>(),
                Arg.Any<GitHubBoardColumn>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CriarIssue()));
        var service = CriarService(acpClient: acpClient, gitHubService: gitHubService);
        var request = CriarRequest();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await service.EnqueueAsync(request);
            await AguardarAsync(async () =>
                gitHubService.ReceivedCalls().Any(call =>
                    call.GetMethodInfo().Name == nameof(IGitHubService.UpdateIssueColumnAsync)));

            await gitHubService.Received(1).UpdateIssueColumnAsync(
                request.RepositoryFullName,
                request.IssueNumber,
                GitHubBoardColumn.InProgress,
                GitHubBoardColumn.InReview,
                Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_ExecucaoComFalha_Quando_ProcessarFila_Entao_NaoMoveIssueERegistraCodigoDeSaida()
    {
        var acpClient = Substitute.For<IAgentAcpClient>();
        acpClient.ExecuteAsync(
                Arg.Any<AgentExecutionRequest>(),
                Arg.Any<IProgress<AgentLogMessage>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentExecutionResult(1, false)));
        var gitHubService = Substitute.For<IGitHubService>();
        var service = CriarService(acpClient: acpClient, gitHubService: gitHubService);
        var request = CriarRequest();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await service.EnqueueAsync(request);
            await AguardarAsync(async () =>
                (await service.GetLogsAsync(request.IssueId)).Any(log => log.Content.Contains("exit code 1")));

            (await service.GetLogsAsync(request.IssueId))
                .ShouldContain(log => log.Content.Contains("exit code 1"));
            await gitHubService.DidNotReceive().UpdateIssueColumnAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<GitHubBoardColumn?>(),
                Arg.Any<GitHubBoardColumn>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_ExecucaoEmAndamento_Quando_Cancelar_Entao_RegistraCancelamento()
    {
        var acpClient = Substitute.For<IAgentAcpClient>();
        acpClient.ExecuteAsync(
                Arg.Any<AgentExecutionRequest>(),
                Arg.Any<IProgress<AgentLogMessage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => AguardarCancelamentoAsync(callInfo.Arg<CancellationToken>()));
        var service = CriarService(acpClient: acpClient);
        var request = CriarRequest();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await service.EnqueueAsync(request);
            await AguardarAsync(async () =>
                (await service.GetLogsAsync(request.IssueId)).Any(log => log.Content.Contains("Starting")));

            await service.CancelAsync(request.IssueId);
            await AguardarAsync(async () =>
                (await service.GetLogsAsync(request.IssueId)).Any(log => log.Content.Contains("cancelled")));

            (await service.GetLogsAsync(request.IssueId))
                .ShouldContain(log => log.Content.Contains("cancelled"));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static AgentOrchestrationService CriarService(
        IAgentAcpClient? acpClient = null,
        IAgentDiscoveryService? discoveryService = null,
        IAgentLogBroadcaster? logBroadcaster = null,
        IAgentLogRepository? agentLogRepository = null,
        IGitHubService? gitHubService = null)
    {
        var repository = agentLogRepository ?? Substitute.For<IAgentLogRepository>();
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new(
            acpClient ?? Substitute.For<IAgentAcpClient>(),
            discoveryService ?? Substitute.For<IAgentDiscoveryService>(),
            logBroadcaster ?? Substitute.For<IAgentLogBroadcaster>(),
            scopeFactory,
            gitHubService ?? Substitute.For<IGitHubService>());
    }

    private static AgentExecutionRequest CriarRequest()
        => new(
            "issue-1",
            42,
            "owner/repo",
            "/workspace/repo",
            "feature/agents",
            "src/Agents",
            "Implementar a orquestração.",
            AgentType.Codex);

    private static IssueDto CriarIssue()
        => new(
            1,
            42,
            "Issue",
            null,
            "open",
            "https://github.com/owner/repo/issues/42",
            "https://github.com/owner/repo/issues/42",
            [],
            GitHubBoardColumn.InReview,
            null,
            "None",
            DateTimeOffset.UtcNow,
            null,
            null);

    private static async Task<AgentExecutionResult> AguardarCancelamentoAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new AgentExecutionResult(0, true);
    }

    private static async Task AguardarAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        (await condition()).ShouldBeTrue();
    }
}
