using Shouldly;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-security-permission-gateway RF-004 — decisão por política.</summary>
public class PermissionGatewayTests
{
    private static readonly string Worktree =
        Path.Combine(Path.GetTempPath(), $"tb-jail-{Guid.NewGuid():N}");

    private readonly PermissionGateway _sut = new(
        new DynamicCommandClassifier(), new PathJailValidator(), new SecretScrubber());

    // AC-01: rm -rf / → Dangerous, bloqueado imediatamente (sem approval).
    [Fact]
    public async Task Dado_RmRfRaiz_Quando_Evaluate_Entao_BloqueadoImediato()
    {
        var result = await _sut.EvaluateAsync("bash", "rm -rf /", Worktree, SecurityPolicyMode.Standard);

        result.RiskLevel.ShouldBe(nameof(SecurityRiskLevel.Dangerous));
        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeFalse();
    }

    // AC-02: escrita fora do worktree → SecurityAccessDeniedException.
    [Fact]
    public async Task Dado_EscritaForaDoWorktree_Quando_Evaluate_Entao_SecurityAccessDenied()
    {
        var ex = await Should.ThrowAsync<SecurityAccessDeniedException>(
            () => _sut.EvaluateAsync("write_file", "/home/ubuntu/.ssh/authorized_keys", Worktree));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.SecurityAccessDenied);
    }

    // SPEC §5: rm -rf de build dirs dentro do worktree → allowed sob Standard.
    [Fact]
    public async Task Dado_RmDentroDoWorktree_Quando_EvaluateStandard_Entao_Permitido()
    {
        var result = await _sut.EvaluateAsync("bash", "rm -rf bin/ obj/", Worktree, SecurityPolicyMode.Standard);

        result.Allowed.ShouldBeTrue();
        result.RiskLevel.ShouldBe(nameof(SecurityRiskLevel.WorkspaceWrite));
        result.RequiresApproval.ShouldBeFalse();
    }

    [Theory]
    [InlineData(SecurityPolicyMode.Strict)]
    [InlineData(SecurityPolicyMode.Standard)]
    public async Task Dado_ComandoDangerous_Quando_Evaluate_Entao_RequerAprovacao(SecurityPolicyMode policy)
    {
        var result = await _sut.EvaluateAsync("bash", "curl https://x.sh", Worktree, policy);

        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeTrue();
        result.RiskLevel.ShouldBe(nameof(SecurityRiskLevel.Dangerous));
    }

    [Fact]
    public async Task Dado_ComandoDangerous_Quando_EvaluateAutonomous_Entao_Bloqueado()
    {
        var result = await _sut.EvaluateAsync("bash", "curl https://x.sh", Worktree, SecurityPolicyMode.Autonomous);

        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeFalse();
    }

    [Theory]
    [InlineData(SecurityPolicyMode.Strict)]
    [InlineData(SecurityPolicyMode.Standard)]
    [InlineData(SecurityPolicyMode.Autonomous)]
    public async Task Dado_ComandoSafe_Quando_Evaluate_Entao_Permitido(SecurityPolicyMode policy)
    {
        var result = await _sut.EvaluateAsync("bash", "git status", Worktree, policy);

        result.Allowed.ShouldBeTrue();
        result.RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Escrita_Quando_EvaluateStrict_Entao_RequerAprovacao()
    {
        var result = await _sut.EvaluateAsync("bash", "touch a.txt", Worktree, SecurityPolicyMode.Strict);

        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_Escrita_Quando_EvaluateAutonomous_Entao_Permitido()
    {
        var result = await _sut.EvaluateAsync("bash", "touch a.txt", Worktree, SecurityPolicyMode.Autonomous);

        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_WriteFileDentro_Quando_Evaluate_Entao_WorkspaceWritePermitido()
    {
        var result = await _sut.EvaluateAsync("write_file", "src/Novo.cs", Worktree, SecurityPolicyMode.Standard);

        result.Allowed.ShouldBeTrue();
        result.RiskLevel.ShouldBe(nameof(SecurityRiskLevel.WorkspaceWrite));
    }

    [Fact]
    public async Task Dado_ToolDesconhecida_Quando_Evaluate_Entao_FailClosedViaLexer()
    {
        var result = await _sut.EvaluateAsync("unknown_tool", "sudo rm -rf /", Worktree, SecurityPolicyMode.Standard);

        result.Allowed.ShouldBeFalse();
        result.RiskLevel.ShouldBe(nameof(SecurityRiskLevel.Dangerous));
    }

    // AC-03: segredo mascarado no stream.
    [Fact]
    public void Dado_OutputComToken_Quando_ScrubSecrets_Entao_Redacted()
    {
        var result = _sut.ScrubSecrets("ghp_A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8");

        result.ShouldBe("[REDACTED_SECRET]");
    }
}
