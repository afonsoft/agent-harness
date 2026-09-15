using Taskboard.Agents;
using Taskboard.Skills;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Skills;

public class AgentSkillDirectoryMapTests
{
    [Theory]
    [InlineData(AgentType.Devin, new[] { ".devin/skills", ".config/devin/skills" })]
    [InlineData(AgentType.Claude, new[] { ".claude/skills" })]
    [InlineData(AgentType.Codex, new[] { ".codex/skills" })]
    [InlineData(AgentType.OpenCode, new[] { ".opencode/skills", ".config/opencode/skills" })]
    [InlineData(AgentType.OpenHands, new[] { ".openhands/skills" })]
    public void Dado_TipoConhecido_Quando_Mapear_Entao_RetornaDiretoriosPadraoEExtras(
        AgentType agentType,
        string[] expectedRelative)
    {
        // Covers RF-001: default map plus extra directories per CLI
        var home = Path.Join(Path.GetTempPath(), "tb-home");

        var directories = AgentSkillDirectoryMap.GetSkillDirectories(agentType, home);

        directories.Select(d => Path.GetRelativePath(home, d).Replace('\\', '/'))
            .ShouldBe(expectedRelative);
    }

    [Fact]
    public void Dado_TipoDesconhecido_Quando_Mapear_Entao_UsaNomeEnumEmLowercase()
    {
        // Covers RF-001 fallback: ~/.{executable-name}/skills
        var home = Path.Join(Path.GetTempPath(), "tb-home");

        var directories = AgentSkillDirectoryMap.GetSkillDirectories((AgentType)999, home);

        directories.Count.ShouldBe(1);
        directories[0].ShouldBe(Path.Join(home, ".999", "skills"));
    }
}
