using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261020-session-history-parity + incidente 2026-10-23: tripwire de
/// regressão — a tabela de sessões do Agents.razor não pode usar
/// <c>@key="session.TranscriptPath"</c>: sessões devin compartilham o mesmo
/// TranscriptPath (<c>~/.local/share/devin/cli/sessions.db</c> guarda todas)
/// e o diff do Blazor aborta com "same key value", derrubando o renderer
/// (cascata: "no event handler associated" nos eventos seguintes).
/// A chave correta é a identidade da sessão: cli + sessionId.
/// </summary>
public class SessionRowKeyGuardTests
{
    private static string AgentsRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "Agents.razor");
    }

    [Fact]
    public void Dado_TabelaDeSessoes_Quando_LeFonte_Entao_TrUsaChaveComposta()
    {
        var source = File.ReadAllText(AgentsRazorPath());

        source.ShouldContain("""<tr @key="SessionRowKey(session)">""");
        source.ShouldNotContain("""@key="session.TranscriptPath""");
    }

    [Fact]
    public void Dado_SessionRowKey_Quando_LeFonte_Entao_CompoemCliESessionId()
    {
        // TranscriptPath colapsa para db-backed CLIs (devin); cli+sessionId é
        // a identidade única mesmo quando o agrupamento mistura CLIs (group=repo).
        var source = File.ReadAllText(AgentsRazorPath());

        source.ShouldContain("SessionRowKey(SessionIndexEntryDto s) => s.Cli + \"@\" + s.SessionId");
    }
}
