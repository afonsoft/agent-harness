using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Descobre agentes CLI instalados no servidor a partir do PATH.
/// SPEC-20260928-ai-code-generic-cli RF-001: todos os <see cref="AgentType"/>
/// mapeados via <see cref="AgentCliMap"/> (OpenHands é o único sem
/// <see cref="AgentCliKind"/> — binário declarado inline). Versões vêm do
/// <see cref="CliProbeSnapshotService"/> — nunca bloqueia em subprocesso.
/// </summary>
public sealed class AgentDiscoveryService : IAgentDiscoveryService
{
    /// <summary>CLIs com sessão ACP estruturada; as demais usam transporte PTY.</summary>
    private static bool AcpCapable(AgentType type) => AgentCliMap.SupportsAcp(type);

    private static readonly IReadOnlyDictionary<AgentType, string> KnownAgents = BuildKnownAgents();

    private static readonly IReadOnlyDictionary<AgentType, string> KnownDescriptions =
        new Dictionary<AgentType, string>
        {
            [AgentType.Devin] = "Devin CLI for agentic coding",
            [AgentType.Claude] = "Claude Code integration",
            [AgentType.Codex] = "OpenAI Codex CLI for code generation",
            [AgentType.OpenCode] = "OpenCode agentic IDE",
            [AgentType.OpenHands] = "OpenHands autonomous software engineer",
            [AgentType.Antigravity] = "Google Antigravity CLI (agy)",
            [AgentType.Kimi] = "Kimi Code agentic CLI",
            [AgentType.Grok] = "Grok (x.ai) CLI",
            [AgentType.Aider] = "Aider pair-programming CLI",
            [AgentType.Cline] = "Cline autonomous coding agent",
            [AgentType.Continue] = "Continue.dev CLI (cn)",
            [AgentType.Copilot] = "GitHub Copilot CLI",
            [AgentType.Qwen] = "Qwen Code agentic CLI",
            [AgentType.Kiro] = "Kiro agentic CLI",
        };

    private readonly CliProbeSnapshotService? _snapshot;
    private readonly Func<string, string?> _locator;

    public AgentDiscoveryService(
        CliProbeSnapshotService? snapshot = null,
        Func<string, string?>? executableLocator = null)
    {
        _snapshot = snapshot;
        _locator = executableLocator ?? PathSearch.FindExecutable;
    }

    public Task<IReadOnlyList<AgentInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var agents = new List<AgentInfo>();

        foreach (var (type, name) in KnownAgents)
        {
            // SPEC-20261004 RF-005: probe the binary plus any declared aliases;
            // a declared required command missing on PATH means not installed.
            var spec = AgentCliMap.CliKindFor(type) is { } cliKind ? AgentCliMap.GetSpec(cliKind) : null;
            var executablePath = spec is null
                ? _locator(name)
                : spec.DetectionNames.Select(_locator).FirstOrDefault(p => p is not null);
            var requiredMet = spec?.RequiredCommands is not { Count: > 0 } required
                || required.All(r => _locator(r) is not null);
            if (!requiredMet)
            {
                executablePath = null;
            }

            KnownDescriptions.TryGetValue(type, out var description);
            // ACP-capable CLIs keep the structured session flag; every
            // resolvable CLI can still open a PTY terminal thread (RF-003).
            var supportsSession = AcpCapable(type);
            var transport = supportsSession ? "acp" : "pty";
            if (executablePath is null)
            {
                agents.Add(new AgentInfo(
                    name, string.Empty, type, AgentStatus.Unavailable, null, description,
                    supportsSession, Transport: transport));
                continue;
            }

            // Version comes from the probe snapshot (SPEC-20260928) — the
            // synchronous `--version` subprocess was removed by RF-004.
            // OpenHands has no AgentCliKind → no version probe.
            var version = AgentCliMap.CliKindFor(type) is { } kind
                ? _snapshot?.GetVersion(kind)
                : null;
            agents.Add(new AgentInfo(
                name, executablePath, type, AgentStatus.Available, version, description,
                supportsSession, Transport: transport));
        }

        return Task.FromResult<IReadOnlyList<AgentInfo>>(agents.AsReadOnly());
    }

    public string? ResolveExecutablePath(AgentType agentType)
    {
        if (!KnownAgents.TryGetValue(agentType, out var name))
        {
            return null;
        }

        var spec = AgentCliMap.CliKindFor(agentType) is { } kind ? AgentCliMap.GetSpec(kind) : null;
        return spec is null
            ? _locator(name)
            : spec.DetectionNames.Select(_locator).FirstOrDefault(p => p is not null);
    }

    /// <summary>
    /// Native transport of an <see cref="AgentType"/> — ACP-capable CLIs keep
    /// "acp"; every other known type talks over PTY.
    /// </summary>
    public static string TransportFor(AgentType type) =>
        AcpCapable(type) ? "acp" : "pty";

    private static IReadOnlyDictionary<AgentType, string> BuildKnownAgents()
    {
        var map = new Dictionary<AgentType, string>();
        foreach (var (type, spec) in Enum.GetValues<AgentType>()
            .Select(type => (Type: type, Spec: AgentCliMap.CliKindFor(type) is { } kind ? AgentCliMap.GetSpec(kind) : null))
            .Where(t => t.Spec is not null))
        {
            map[type] = spec!.Binary;
        }

        // OpenHands is the only AgentType without an AgentCliKind entry.
        map[AgentType.OpenHands] = "openhands";
        return map;
    }
}
