using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Taskboard.Agents;
using Taskboard.GitHub;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Orquestra a execução de agentes CLI em background usando Channel e SignalR.
/// </summary>
public sealed class AgentOrchestrationService : BackgroundService, IAgentOrchestrationService
{
    private readonly IAgentAcpClient _acpClient;
    private readonly IAgentDiscoveryService _discoveryService;
    private readonly IAgentLogBroadcaster _logBroadcaster;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly Channel<AgentExecutionRequest> _channel = Channel.CreateUnbounded<AgentExecutionRequest>();
    private readonly ConcurrentDictionary<string, RunningJob> _running = new();
    private readonly ConcurrentDictionary<string, List<AgentLogMessage>> _logs = new();

    public AgentOrchestrationService(
        IAgentAcpClient acpClient,
        IAgentDiscoveryService discoveryService,
        IAgentLogBroadcaster logBroadcaster,
        IServiceScopeFactory serviceScopeFactory,
        IGitHubService gitHubService)
    {
        _acpClient = acpClient;
        _discoveryService = discoveryService;
        _logBroadcaster = logBroadcaster;
        _serviceScopeFactory = serviceScopeFactory;
        _gitHubService = gitHubService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(async () => await RunAsync(request, stoppingToken), stoppingToken);
        }
    }

    public async Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await _discoveryService.DiscoverAsync(cancellationToken);
        var busyTypes = _running.Values
            .Select(r => r.Request.AgentType)
            .ToHashSet();

        return discovered
            .Select(a => a with
            {
                Status = a.Status == AgentStatus.Available && busyTypes.Contains(a.Type)
                    ? AgentStatus.Busy
                    : a.Status
            })
            .ToList()
            .AsReadOnly();
    }

    public Task EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureLogList(request.IssueId);
        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Queued {request.AgentType} for issue {request.IssueId}."));
        _channel.Writer.TryWrite(request);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var list = _logs.GetValueOrDefault(issueId);
        if (list is not null && list.Count > 0)
        {
            lock (list)
            {
                return list.ToList().AsReadOnly();
            }
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentLogRepository>();
        return await repository.GetByIssueIdAsync(issueId, cancellationToken);
    }

    public Task CancelAsync(string issueId, CancellationToken cancellationToken = default)
    {
        if (_running.TryGetValue(issueId, out var job))
        {
            job.CancellationTokenSource.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task RunAsync(AgentExecutionRequest request, CancellationToken stoppingToken)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var job = new RunningJob(request, cancellationTokenSource);
        _running[request.IssueId] = job;
        EnsureLogList(request.IssueId);

        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Starting {request.AgentType} on {request.RepoPath}..."));

        var progress = new Progress<AgentLogMessage>(async message =>
        {
            AppendLog(message.IssueId, message);
            await _logBroadcaster.BroadcastAsync(message);
        });

        try
        {
            var result = await _acpClient.ExecuteAsync(request, progress, cancellationTokenSource.Token);

            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent finished with exit code {result.ExitCode}."));

            if (result.IsSuccess)
            {
                await MoveToReviewAsync(request);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, "Agent execution was cancelled."));
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent error: {ex.Message}"));
        }
        finally
        {
            _running.TryRemove(request.IssueId, out _);
            cancellationTokenSource.Dispose();
        }
    }

    private async Task MoveToReviewAsync(AgentExecutionRequest request)
    {
        try
        {
            await _gitHubService.UpdateIssueColumnAsync(
                request.RepositoryFullName,
                request.IssueNumber,
                GitHubBoardColumn.InProgress,
                GitHubBoardColumn.InReview);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Failed to move issue to review: {ex.Message}"));
        }
    }

    private void EnsureLogList(string issueId)
    {
        _logs.GetOrAdd(issueId, _ => []);
    }

    private void AppendLog(string issueId, AgentLogMessage message)
    {
        var list = _logs.GetOrAdd(issueId, _ => []);
        lock (list)
        {
            list.Add(message);
        }

        _ = _logBroadcaster.BroadcastAsync(message);
        _ = Task.Run(async () =>
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentLogRepository>();
            await repository.AppendAsync(message);
        });
    }

    private sealed class RunningJob
    {
        public RunningJob(AgentExecutionRequest request, CancellationTokenSource cancellationTokenSource)
        {
            Request = request;
            CancellationTokenSource = cancellationTokenSource;
        }

        public AgentExecutionRequest Request { get; }
        public CancellationTokenSource CancellationTokenSource { get; }
    }
}
