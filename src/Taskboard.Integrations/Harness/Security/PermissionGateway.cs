using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness.Security;

/// <summary>
/// Composes the command classifier, path jail and secret scrubber into the
/// pre-dispatch permission decision (SPEC-20260919-harness-security-permission-gateway
/// RF-001..RF-004). Fail-closed everywhere: unknown tools classify via the
/// shell lexer and unparseable input is Dangerous.
/// </summary>
public sealed class PermissionGateway : IPermissionGateway
{
    private static readonly HashSet<string> ReadToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "read_file", "get_file", "list", "list_files", "search", "search_files", "grep", "glob"
    };

    private static readonly HashSet<string> WriteToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "write", "write_file", "edit", "create", "create_file", "delete", "delete_file", "mkdir"
    };

    private static readonly HashSet<string> ShellToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash", "shell", "terminal", "exec", "run_command", "process"
    };

    private readonly ICommandRiskClassifier _classifier;
    private readonly SensitivePathDenylist _denylist;

    public PermissionGateway(ICommandRiskClassifier classifier, SensitivePathDenylist? denylist = null)
    {
        _classifier = classifier;
        _denylist = denylist ?? SensitivePathDenylist.Shared;
    }

    public Task<SecurityEvaluationDto> EvaluateAsync(
        string toolName,
        string command,
        string worktreePath,
        SecurityPolicyMode policy = SecurityPolicyMode.Standard,
        CancellationToken cancellationToken = default)
    {
        var assessment = Assess(toolName, command, worktreePath);
        return Task.FromResult(Decide(assessment, policy));
    }

    public string ScrubSecrets(string output) => SecretScrubber.Scrub(output);

    private CommandRiskAssessment Assess(string toolName, string command, string worktreePath)
    {
        if (ReadToolNames.Contains(toolName))
        {
            // Path jail: lança SecurityAccessDeniedException fora do worktree
            // ou em credential-path (SPEC-20261023-p0-agent-hardening RF-A01).
            PathJailValidator.Validate(command, worktreePath, _denylist);
            return new(SecurityRiskLevel.Safe, "Leitura confinada ao worktree.");
        }

        if (WriteToolNames.Contains(toolName))
        {
            PathJailValidator.Validate(command, worktreePath, _denylist);
            return new(SecurityRiskLevel.WorkspaceWrite, "Escrita confinada ao worktree.");
        }

        // bash/exec e tools desconhecidas passam pelo lexer (fail-closed).
        return _classifier.Classify(command, worktreePath);
    }

    private static SecurityEvaluationDto Decide(
        CommandRiskAssessment assessment, SecurityPolicyMode policy)
    {
        // Escape de sandbox e credential-path são deny duro — approval
        // humana não torna seguro (SPEC-20261023-p0-agent-hardening RF-A02).
        if (assessment.EscapesSandbox || assessment.DeniesCredentialPath)
        {
            var block = assessment.DeniesCredentialPath ? "credential-path" : "escape do sandbox";
            return new(false, nameof(SecurityRiskLevel.Dangerous), false,
                $"{assessment.Reason} (bloqueio permanente: {block})");
        }

        return (assessment.RiskLevel, policy) switch
        {
            (SecurityRiskLevel.Safe, _) =>
                new(true, nameof(SecurityRiskLevel.Safe), false, assessment.Reason),

            (SecurityRiskLevel.WorkspaceWrite, SecurityPolicyMode.Strict) =>
                new(false, nameof(SecurityRiskLevel.WorkspaceWrite), true,
                    $"{assessment.Reason} Política Strict exige aprovação para escrita."),
            (SecurityRiskLevel.WorkspaceWrite, _) =>
                new(true, nameof(SecurityRiskLevel.WorkspaceWrite), false, assessment.Reason),

            (SecurityRiskLevel.Dangerous, SecurityPolicyMode.Autonomous) =>
                new(false, nameof(SecurityRiskLevel.Dangerous), false,
                    $"{assessment.Reason} Política Autonomous bloqueia comandos perigosos."),
            (SecurityRiskLevel.Dangerous, _) =>
                new(false, nameof(SecurityRiskLevel.Dangerous), true,
                    $"{assessment.Reason} Requer aprovação humana."),

            _ => new(false, nameof(SecurityRiskLevel.Dangerous), false, "Decisão indeterminada — fail-closed.")
        };
    }
}
