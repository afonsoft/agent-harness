using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261022-mailbox-wake: wake on addressed delivery — resolution,
/// preference gate, 60s throttle, broadcast skip and silent misses.</summary>
public sealed class MailboxWakeServiceTests
{
    private sealed record Host(
        MailboxWakeService Service,
        IAgentHibernationRepository Records,
        IAgentHibernationService Hibernation,
        IDelegationTaskRepository Tasks,
        IWorktreeSessionRepository Sessions,
        IRepository<UserPreference> Users);

    private static Host CreateHost()
    {
        var records = Substitute.For<IAgentHibernationRepository>();
        var hibernation = Substitute.For<IAgentHibernationService>();
        var tasks = Substitute.For<IDelegationTaskRepository>();
        var sessions = Substitute.For<IWorktreeSessionRepository>();
        var users = Substitute.For<IRepository<UserPreference>>();
        users.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton(records);
        services.AddSingleton(hibernation);
        services.AddSingleton(tasks);
        services.AddSingleton(sessions);
        services.AddSingleton(users);
        var provider = services.BuildServiceProvider();

        return new Host(
            new MailboxWakeService(provider, NullLogger<MailboxWakeService>.Instance),
            records, hibernation, tasks, sessions, users);
    }

    private static AgentHibernationDto Hibernated(
        string key, string cli, string? cwd = null, DateTime? lastAutoResume = null) =>
        new(key, "local", cli, true, DateTime.UtcNow, DateTime.UtcNow,
            ["cli", "--resume"], null, cwd, 0, false, lastAutoResume);

    [Fact]
    public async Task Dado_CliHibernado_Quando_WakeComCli_Entao_ResumeAuto()
    {
        // AC: sessão hibernada + destinatário <cli> → ResumeAsync(auto, "mailbox").
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex")]);
        host.Hibernation.ResumeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new ResumeOutcome(true, call.ArgAt<string>(1)));

        await host.Service.WakeForRecipientAsync("codex");

        await host.Records.Received(1).MarkAutoResumeAttemptAsync(
            "term:t1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await host.Hibernation.Received(1).ResumeAsync(
            "local", "term:t1", true, "mailbox", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("@all")]
    [InlineData("@idle")]
    [InlineData(null)]
    public async Task Dado_Broadcast_Quando_Wake_Entao_SemResume(string? recipient)
    {
        // RF-003: endereços de grupo nunca acordam sessões.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex")]);

        await host.Service.WakeForRecipientAsync(recipient);

        await host.Hibernation.DidNotReceiveWithAnyArgs().ResumeAsync(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task Dado_ResumeRecente_Quando_Wake_Entao_ThrottleSuprime()
    {
        // AC: duas mensagens dentro de 60s → só uma tentativa.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated(
                "term:t1", "codex", lastAutoResume: DateTime.UtcNow.AddSeconds(-30))]);

        await host.Service.WakeForRecipientAsync("codex");

        await host.Hibernation.DidNotReceiveWithAnyArgs().ResumeAsync(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task Dado_PrefDesligada_Quando_Wake_Entao_SemResume()
    {
        // AC: AutoResumeOnMailbox = false → delivery gravada, sessão continua zzz.
        var host = CreateHost();
        host.Users.ListAsync(Arg.Any<CancellationToken>())
            .Returns([new UserPreference(Guid.NewGuid())
            {
                AutoResumeOnMailbox = false,
            }]);
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex")]);

        await host.Service.WakeForRecipientAsync("codex");

        await host.Hibernation.DidNotReceiveWithAnyArgs().ResumeAsync(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task Dado_WorktreeAddress_Quando_Wake_Entao_ResumePorCwd()
    {
        // RF-003: @worktree:<runId> resolve a sessão pela cwd do worktree.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([
                Hibernated("term:t1", "codex", "/repo/.worktrees/wt-1"),
                Hibernated("term:t2", "codex", "/outro/dir"),
            ]);
        host.Sessions.GetByRunIdAsync("run1", Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt-1", "run1", "/repo/.worktrees/wt-1/", "wt/issue-1", "active",
                "/repo", "main", null, false, DateTime.UtcNow, DateTime.UtcNow, 1));
        host.Hibernation.ResumeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new ResumeOutcome(true, call.ArgAt<string>(1)));

        await host.Service.WakeForRecipientAsync("@worktree:run1");

        await host.Hibernation.Received(1).ResumeAsync(
            "local", "term:t1", true, "mailbox", Arg.Any<CancellationToken>());
        await host.Hibernation.DidNotReceive().ResumeAsync(
            Arg.Any<string>(), "term:t2", Arg.Any<bool>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskIdComWorktree_Quando_Wake_Entao_ResumeDaSessao()
    {
        // <taskId> → task.WorktreeRunId → sessão → join por cwd.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex", "/repo/.worktrees/wt-1")]);
        host.Tasks.GetAsync("task-9", Arg.Any<CancellationToken>())
            .Returns(new DelegationTaskDto(
                "task-9", "scope", "p", "codex", [], null, null,
                true, "run1", "/repo/.worktrees/wt-1", "/repo", null,
                DelegationTaskStatus.Running, null, null,
                DateTime.UtcNow, null, null, null));
        host.Sessions.GetByRunIdAsync("run1", Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt-1", "run1", "/repo/.worktrees/wt-1", "wt/issue-1", "active",
                "/repo", "main", null, false, DateTime.UtcNow, DateTime.UtcNow, 1));
        host.Hibernation.ResumeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => new ResumeOutcome(true, call.ArgAt<string>(1)));

        await host.Service.WakeForRecipientAsync("task-9");

        await host.Hibernation.Received(1).ResumeAsync(
            "local", "term:t1", true, "mailbox", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DestinatarioSemSessao_Quando_Wake_Entao_Ignorado()
    {
        // Destinatário não resolve a nenhuma sessão hibernada → skip silencioso.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex")]);

        await host.Service.WakeForRecipientAsync("opencode");

        await host.Hibernation.DidNotReceiveWithAnyArgs().ResumeAsync(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task Dado_ResumeFalha_Quando_Wake_Entao_NaoPropaga()
    {
        // AC: falha no resume não quebra o fluxo — outcome falsificado, sem throw.
        var host = CreateHost();
        host.Records.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Hibernated("term:t1", "codex")]);
        host.Hibernation.ResumeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ResumeOutcome(false, "term:t1", "respawn failed"));

        await host.Service.WakeForRecipientAsync("codex");

        await host.Hibernation.Received(1).ResumeAsync(
            "local", "term:t1", true, "mailbox", Arg.Any<CancellationToken>());
    }
}
