using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Harness;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// SPEC-20261023 RF-C01: run preflight doctor — pure probes, no side effects
/// beyond the probes themselves. Per-check 2s budget, global 8s bound; a
/// failed probe degrades to a warning, never crashes the report.
/// </summary>
public sealed class PreflightService(
    IAgentCliStatusService cliStatus,
    IRepository<ChatProvider> providers,
    IMcpClientManager mcp,
    IConfiguration configuration,
    Taskboard.Application.Contracts.Chat.IChatToolSandbox? sandbox = null) : IPreflightService
{
    internal static readonly TimeSpan CheckBudget = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan GlobalBudget = TimeSpan.FromSeconds(8);
    internal const long DiskWarnBytes = 1L << 30;    // 1 GiB
    internal const long DiskBlockBytes = 100L << 20; // 100 MiB

    private static readonly string[] ProviderKeyVars = ["OPENAI_API_KEY", "ANTHROPIC_API_KEY", "OPENROUTER_API_KEY"];

    public async Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default)
    {
        using var global = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        global.CancelAfter(GlobalBudget);

        var checks = new List<PreflightCheck>();
        (string Name, Func<CancellationToken, Task<IEnumerable<PreflightCheck>>> Run)[] probes =
        [
            ("agent-cli", CheckAgentClisAsync),
            ("worktree-root", _ => Task.FromResult(CheckWorktreeRoot())),
            ("mcp", CheckMcpAsync),
            ("chat-provider", CheckChatProviderAsync),
            ("chat-sandbox", CheckChatSandboxAsync),
            ("disk", _ => Task.FromResult(CheckDisk())),
        ];

        foreach (var (name, run) in probes)
        {
            try
            {
                using var perCheck = CancellationTokenSource.CreateLinkedTokenSource(global.Token);
                perCheck.CancelAfter(CheckBudget);
                checks.AddRange(await run(perCheck.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                checks.Add(new PreflightCheck(name, PreflightCheckStatus.Warning,
                    "probe timed out", "retry or check the component directly"));
                if (global.IsCancellationRequested)
                {
                    checks.Add(new PreflightCheck("preflight", PreflightCheckStatus.Warning,
                        "global probe budget (8s) exhausted — remaining checks skipped"));
                    break;
                }
            }
            catch (Exception ex)
            {
                checks.Add(new PreflightCheck(name, PreflightCheckStatus.Warning,
                    $"probe failed: {ex.Message}"));
            }
        }

        return PreflightReport.From(checks);
    }

    private async Task<IEnumerable<PreflightCheck>> CheckAgentClisAsync(CancellationToken ct)
    {
        var statuses = await cliStatus.GetStatusAsync(ct).ConfigureAwait(false);
        var checks = new List<PreflightCheck>();
        if (statuses.Count == 0 || statuses.All(s => !s.Installed))
        {
            checks.Add(new PreflightCheck("agent-cli", PreflightCheckStatus.Blocked,
                "no agent CLI installed on PATH",
                "install one (e.g. npm i -g @anthropic/claude-code) then retry"));
        }

        foreach (var s in statuses)
        {
            var name = $"cli:{s.Binary}";
            if (!s.Installed)
            {
                checks.Add(new(name, PreflightCheckStatus.Warning,
                    $"{s.DisplayName} not installed", s.InstallHint));
            }
            else if (!s.PrerequisiteMet)
            {
                checks.Add(new(name, PreflightCheckStatus.Warning,
                    $"{s.DisplayName} prerequisite missing", $"install {s.RequiredTool}"));
            }
            else if (s.AuthStatus is not AgentCliAuthStatus.Authenticated)
            {
                checks.Add(new(name, PreflightCheckStatus.Warning,
                    $"{s.DisplayName} installed but not authenticated", s.LoginCommand));
            }
            else
            {
                checks.Add(new(name, PreflightCheckStatus.Ok,
                    $"{s.DisplayName} {(s.Version is { } v ? $"v{v}" : "installed")} · authenticated"));
            }
        }

        return checks;
    }

    private IEnumerable<PreflightCheck> CheckWorktreeRoot()
    {
        var root = WorktreePaths.ResolveRoot(configuration["Taskboard:WorktreeRoot"], HomeDir());
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Join(root, $".preflight-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return [new PreflightCheck("worktree-root", PreflightCheckStatus.Ok, $"writable: {root}")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new PreflightCheck("worktree-root", PreflightCheckStatus.Blocked,
                $"not writable: {root} ({ex.Message})",
                "fix permissions or set Taskboard:WorktreeRoot to a writable dir")];
        }
    }

    private async Task<IEnumerable<PreflightCheck>> CheckMcpAsync(CancellationToken ct)
    {
        var configured = mcp.GetServers();
        if (configured.Count == 0)
        {
            return [new PreflightCheck("mcp", PreflightCheckStatus.Ok, "no MCP servers configured")];
        }

        // Bounded lazy connect — failures degrade to per-server status, never throw.
        await mcp.GetToolsAsync(ct).ConfigureAwait(false);
        var down = mcp.GetServers().Where(s => !s.Healthy).Select(s => s.Name).ToList();
        return down.Count == 0
            ? [new PreflightCheck("mcp", PreflightCheckStatus.Ok, $"{configured.Count} MCP server(s) reachable")]
            : [new PreflightCheck("mcp", PreflightCheckStatus.Warning,
                $"unreachable MCP server(s): {string.Join(", ", down)}",
                "check the server config under Taskboard:Chat:Mcp")];
    }

    private async Task<IEnumerable<PreflightCheck>> CheckChatProviderAsync(CancellationToken ct)
    {
        var rows = await providers.ListAsync(ct).ConfigureAwait(false);
        var withKey = rows.Count(p => !string.IsNullOrWhiteSpace(p.ApiKey));
        var hasEnvKey = ProviderKeyVars.Any(k => !string.IsNullOrWhiteSpace(configuration[k]));
        if (withKey == 0 && !hasEnvKey)
        {
            return [new PreflightCheck("chat-provider", PreflightCheckStatus.Blocked,
                "no chat provider API key configured",
                $"add a provider key in Settings → AI Providers or set one of {string.Join("/", ProviderKeyVars)}")];
        }

        return [new PreflightCheck("chat-provider", PreflightCheckStatus.Ok,
            withKey > 0 ? $"{withKey} provider(s) with key" : "provider key via environment")];
    }

    // SPEC-20261025 C2: only surfaces when the operator enabled sandbox mode —
    // off means host exec by design, so there is nothing to warn about.
    private async Task<IEnumerable<PreflightCheck>> CheckChatSandboxAsync(CancellationToken ct)
    {
        if (sandbox is null || !sandbox.Enabled)
        {
            return [];
        }

        return await sandbox.IsAvailableAsync(ct).ConfigureAwait(false)
            ? [new PreflightCheck("chat-sandbox", PreflightCheckStatus.Ok,
                $"{sandbox.Name} reachable — chat exec tools run containerized")]
            : [new PreflightCheck("chat-sandbox", PreflightCheckStatus.Warning,
                $"{sandbox.Name} unreachable — chat exec tools fall back to host",
                "start docker or set Taskboard:Chat:Sandbox=off")];
    }

    private IEnumerable<PreflightCheck> CheckDisk()
    {
        var root = WorktreePaths.ResolveRoot(configuration["Taskboard:WorktreeRoot"], HomeDir());
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var free = drive.AvailableFreeSpace;
            var mb = free / (1024 * 1024);
            return free < DiskBlockBytes
                ? [new PreflightCheck("disk", PreflightCheckStatus.Blocked,
                    $"{mb} MB free — runs will fail on checkout/write", "free disk space")]
                : free < DiskWarnBytes
                    ? [new PreflightCheck("disk", PreflightCheckStatus.Warning,
                        $"{mb} MB free — below 1 GiB threshold", "free disk space")]
                    : [new PreflightCheck("disk", PreflightCheckStatus.Ok, $"{mb} MB free")];
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return [new PreflightCheck("disk", PreflightCheckStatus.Warning,
                $"disk probe failed: {ex.Message}")];
        }
    }

    private string HomeDir() =>
        configuration["Taskboard:HomeDir"]
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
