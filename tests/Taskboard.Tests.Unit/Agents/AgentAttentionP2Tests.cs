using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model P2: pattern table match, OSC/matcher
/// debounce, hook installer merge/uninstall, hook token rotation.
/// </summary>
public class AgentAttentionP2Tests
{
    // ---------- RF-005: pattern table ----------

    [Fact]
    public void Dado_PromptPermissaoClaude_Quando_Match_Entao_Permission()
    {
        var match = AgentCliAttentionPatterns.Match(
            AgentCliKind.Claude, "Do you want to proceed?\n");

        match.ShouldNotBeNull();
        match.Kind.ShouldBe(AgentOutputPatternKind.Permission);
    }

    [Fact]
    public void Dado_ApprovalCodex_Quando_Match_Entao_Permission()
    {
        var match = AgentCliAttentionPatterns.Match(
            AgentCliKind.Codex, "allow command \"rm -rf x\"? [y/n]");

        match.ShouldNotBeNull();
        match.Kind.ShouldBe(AgentOutputPatternKind.Permission);
    }

    [Fact]
    public void Dado_SaidaComum_Quando_MatchGenerico_Entao_NuloOuQuestion()
    {
        AgentCliAttentionPatterns.Match(AgentCliKind.Aider, "building files...")
            .ShouldBeNull();
    }

    [Fact]
    public void Dado_BinarioConhecido_Quando_KindForBinary_Entao_Resolve()
    {
        AgentCliAttentionPatterns.KindForBinary("/usr/local/bin/claude")
            .ShouldBe(AgentCliKind.Claude);
        AgentCliAttentionPatterns.KindForBinary("codex.exe")
            .ShouldBe(AgentCliKind.Codex);
        AgentCliAttentionPatterns.KindForBinary("bash").ShouldBeNull();
    }

    // ---------- RF-005/RF-006: output sink ----------

    [Fact]
    public async Task Dado_TailComPergunta_Quando_Observe_Entao_PublicaWaitingYou()
    {
        var attention = Substitute.For<IAgentAttentionService>();
        var sink = new AgentOutputAttentionService(attention);

        sink.ObserveOutput("term:s1", "claude",
            "working…\nDo you want to proceed?\n");

        await attention.Received(1).PublishAsync(
            Arg.Is<AttentionSignal>(s =>
                s.SessionKey == "term:s1"
                && s.Kind == AttentionSignalKind.OutputPattern
                && s.SuggestedState == AgentAttentionState.WaitingYou),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_BurstDeChunks_Quando_Observe_Entao_DebounceUmaPublicacao()
    {
        var attention = Substitute.For<IAgentAttentionService>();
        var sink = new AgentOutputAttentionService(attention);

        for (var i = 0; i < 5; i++)
        {
            sink.ObserveOutput("term:s2", "claude", "chunk " + i);
        }

        await attention.Received(1).PublishAsync(
            Arg.Any<AttentionSignal>(), Arg.Any<CancellationToken>());
    }

    // ---------- RF-003: hook tokens ----------

    [Fact]
    public void Dado_TokenEmitido_Quando_Validate_Entao_RetornaSessao()
    {
        var tokens = new AgentHookTokenService();
        var token = tokens.Issue("issue:42");

        tokens.Validate(token).ShouldBe("issue:42");
        tokens.Validate("bogus").ShouldBeNull();
    }

    [Fact]
    public void Dado_Rotacao_Quando_IssueDuasVezes_Entao_TokenAntigoInvalido()
    {
        var tokens = new AgentHookTokenService();
        var old = tokens.Issue("term:x");
        var rotated = tokens.Issue("term:x");

        tokens.Validate(old).ShouldBeNull();
        tokens.Validate(rotated).ShouldBe("term:x");
    }

    [Fact]
    public void Dado_Revoke_Quando_Validate_Entao_Invalido()
    {
        var tokens = new AgentHookTokenService();
        var token = tokens.Issue("term:y");
        tokens.Revoke("term:y");

        tokens.Validate(token).ShouldBeNull();
    }

    // ---------- RF-004: hook installer ----------

    [Fact]
    public async Task Dado_SettingsComHooksUsuario_Quando_Install_Entao_Preserva()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var claudeDir = Path.Combine(dir.FullName, ".claude");
            Directory.CreateDirectory(claudeDir);
            var settingsPath = Path.Combine(claudeDir, "settings.json");
            await File.WriteAllTextAsync(settingsPath, """
                {"hooks":{"SessionStart":[{"matcher":"*","hooks":[{"type":"command","command":"echo mine"}]}]}}
                """);

            var installer = new AgentHookInstaller();
            (await installer.InstallAsync(AgentCliKind.Claude, dir.FullName)).ShouldBeTrue();

            var json = await File.ReadAllTextAsync(settingsPath);
            json.ShouldContain("echo mine");           // user hook preserved
            json.ShouldContain(AgentHookInstaller.Marker);

            // Idempotent — second install is a no-op.
            (await installer.InstallAsync(AgentCliKind.Claude, dir.FullName)).ShouldBeFalse();

            // Uninstall removes only managed entries.
            (await installer.UninstallAsync(AgentCliKind.Claude, dir.FullName)).ShouldBeTrue();
            json = await File.ReadAllTextAsync(settingsPath);
            json.ShouldNotContain(AgentHookInstaller.Marker);
            json.ShouldContain("echo mine");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Dado_ConfigToml_Quando_InstallCodex_Entao_BlocoManaged()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var installer = new AgentHookInstaller();
            (await installer.InstallAsync(AgentCliKind.Codex, dir.FullName)).ShouldBeTrue();
            (await installer.InstallAsync(AgentCliKind.Codex, dir.FullName)).ShouldBeFalse();

            var tomlPath = Path.Combine(dir.FullName, ".codex", "config.toml");
            var toml = await File.ReadAllTextAsync(tomlPath);
            toml.ShouldContain("harness-agent-hook");
            toml.ShouldContain("notify");

            (await installer.UninstallAsync(AgentCliKind.Codex, dir.FullName)).ShouldBeTrue();
            (await File.ReadAllTextAsync(tomlPath)).ShouldNotContain("harness-agent-hook");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
