using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents.Attention;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261104-agent-attention-manifests: manifest parsing (schema, gates,
/// version gates), pure engine evaluation (priority, regions, unknown),
/// source precedence, hot reload and the explain store.
/// </summary>
public class AgentDetectionTests
{
    private static DetectionSnapshot Snap(string content, string? title = null) =>
        new(content, title);

    private static AgentDetectionEngine Engine(params (string Name, string Toml)[] manifests) => new(
        new AgentDetectionManifestLoader(
            manifests.ToDictionary(m => m.Name, m => m.Toml), overrideDir: null));

    private const string Shell = """
        id = "shell"
        [[rules]]
        id = "yn"
        state = "blocked"
        priority = 50
        region = "prompt_box_body"
        visible_blocker = true
        line_regex = ['\[(y|Y)/(n|N)\]\s*$']
        """;

    private const string Claude = """
        id = "claude"
        aliases = ["claude-code"]
        [[rules]]
        id = "interrupt"
        state = "working"
        priority = 900
        region = "bottom_non_empty_lines(8)"
        visible_working = true
        contains = ["esc to interrupt"]
        [[rules]]
        id = "proceed"
        state = "blocked"
        priority = 800
        region = "after_last_horizontal_rule"
        visible_blocker = true
        contains = ["do you want to proceed?"]
        [[rules]]
        id = "prompt"
        state = "idle"
        priority = 500
        region = "prompt_box_body"
        visible_idle = true
        line_regex = ['^\s*❯']
        """;

    // ---------- manifest parsing ----------

    [Fact]
    public void Dado_TomlValido_Quando_Parse_Entao_CompilaRegrasEGates()
    {
        var m = AgentDetectionManifest.Parse(Claude, "test");

        m.Id.ShouldBe("claude");
        m.Aliases.ShouldBe(["claude-code"]);
        m.Rules.Count.ShouldBe(3);
        m.Rules[0].Gate.Contains.ShouldBe(["esc to interrupt"]);
    }

    [Fact]
    public void Dado_ChaveDesconhecida_Quando_Parse_Entao_Throw()
    {
        Should.Throw<ManifestException>(() =>
            AgentDetectionManifest.Parse("id = \"x\"\nbogus = 1", "test"));
    }

    [Fact]
    public void Dado_EstadoInvalido_Quando_Parse_Entao_Throw()
    {
        Should.Throw<ManifestException>(() =>
            AgentDetectionManifest.Parse(
                "id = \"x\"\n[[rules]]\nid=\"r\"\nstate=\"sleepy\"", "test"));
    }

    [Fact]
    public void Dado_RegexInvalida_Quando_Parse_Entao_Throw()
    {
        Should.Throw<ManifestException>(() =>
            AgentDetectionManifest.Parse(
                "id = \"x\"\n[[rules]]\nid=\"r\"\nstate=\"idle\"\nregex=[\"[unclosed\"]", "test"));
    }

    [Fact]
    public void Dado_NotGateVazio_Quando_Parse_Entao_Throw()
    {
        Should.Throw<ManifestException>(() =>
            AgentDetectionManifest.Parse(
                "id = \"x\"\n[[rules]]\nid=\"r\"\nstate=\"idle\"\nnot=[]", "test"));
    }

    // ---------- gate combinators ----------

    [Fact]
    public void Dado_GatesAninhados_Quando_Eval_Entao_AndOrNotRespeitados()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "combo"
            state = "blocked"
            priority = 100
            contains = ["header"]
            all = [
              { any = [
                { line_regex = ['(?i)^\s*❯?\s*yes\b'] },
                { contains = ["opt-in"] },
              ] },
            ]
            not = [
              { contains = ["veto-me"] },
            ]
            """));

        // contains + any(yes) → match
        engine.Evaluate(Snap("header\n  ❯ yes"), "claude").MatchedRuleId.ShouldBe("combo");
        // any(opt-in) → match
        engine.Evaluate(Snap("header\nchoose opt-in"), "claude").MatchedRuleId.ShouldBe("combo");
        // not gate veto → no match
        engine.Evaluate(Snap("header\n❯ yes\nveto-me"), "claude").MatchedRuleId.ShouldBeNull();
        // contains fails → no match
        engine.Evaluate(Snap("other\n❯ yes"), "claude").MatchedRuleId.ShouldBeNull();
    }

    [Fact]
    public void Dado_Contains_Quando_Eval_Entao_CaseInsensitive()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "r"
            state = "working"
            contains = ["esc to cancel"]
            """));

        engine.Evaluate(Snap("ESC TO CANCEL now"), "claude").MatchedRuleId.ShouldBe("r");
    }

    [Fact]
    public void Dado_LineRegex_Quando_Eval_Entao_CadaPatternCasaUmaLinha()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "r"
            state = "blocked"
            line_regex = ['^aaa', '^zzz']
            """));

        engine.Evaluate(Snap("aaa\nmid\nzzz"), "claude").MatchedRuleId.ShouldBe("r");
        // second pattern doesn't anchor a line → fail
        engine.Evaluate(Snap("aaa\nmid zzz mid"), "claude").MatchedRuleId.ShouldBeNull();
    }

    // ---------- priority / unknown / flags ----------

    [Fact]
    public void Dado_RegrasCompetem_Quando_Eval_Entao_MaiorPrioridadeVence()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "low"
            state = "idle"
            priority = 10
            contains = ["prompt"]
            [[rules]]
            id = "high"
            state = "blocked"
            priority = 90
            visible_blocker = true
            contains = ["prompt"]
            """));

        var v = engine.Evaluate(Snap("prompt"), "claude");
        v.MatchedRuleId.ShouldBe("high");
        v.SuggestedState.ShouldBe(AgentAttentionState.WaitingYou); // blocked+visible_blocker
        v.Flags.ShouldContain("visible_blocker");
    }

    [Fact]
    public void Dado_BlockedSemFlag_Quando_Eval_Entao_BlockedNaoWaiting()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "r"
            state = "blocked"
            contains = ["oops"]
            """));

        engine.Evaluate(Snap("oops"), "claude").SuggestedState
            .ShouldBe(AgentAttentionState.Blocked);
    }

    [Fact]
    public void Dado_SemMatch_Quando_Eval_Entao_SuggestedNullSemRegra()
    {
        var engine = Engine(("shell.toml", Shell));

        var v = engine.Evaluate(Snap("plain output"), "claude");
        v.MatchedRuleId.ShouldBeNull();
        v.SuggestedState.ShouldBeNull();
        v.ManifestSource.ShouldBe("bundled");
        v.ManifestId.ShouldBe("shell");
    }

    [Fact]
    public void Dado_SkipStateUpdate_Quando_Eval_Entao_MatchedSemEstado()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "overlay"
            state = "unknown"
            priority = 100
            skip_state_update = true
            contains = ["transcript"]
            """));

        var v = engine.Evaluate(Snap("transcript"), "claude");
        v.MatchedRuleId.ShouldBe("overlay");
        v.SuggestedState.ShouldBeNull();
    }

    // ---------- regions ----------

    [Fact]
    public void Dado_OscTitle_Quando_Eval_Entao_CasaRegiaoTitle()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "busy"
            state = "working"
            region = "osc_title"
            visible_working = true
            regex = ['^⠋']
            """));

        engine.Evaluate(Snap("nothing", title: "⠋ compiling"), "claude")
            .MatchedRuleId.ShouldBe("busy");
        engine.Evaluate(Snap("nothing", title: "idle"), "claude")
            .MatchedRuleId.ShouldBeNull();
    }

    [Fact]
    public void Dado_AfterLastRule_Quando_Eval_Entao_SoTextoAposRegraConta()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "proceed"
            state = "blocked"
            region = "after_last_horizontal_rule"
            contains = ["do you want to proceed?"]
            """));

        // prompt above the rule → no match
        engine.Evaluate(Snap("do you want to proceed?\n──────\noutput"), "claude")
            .MatchedRuleId.ShouldBeNull();
        // prompt below the rule → match
        engine.Evaluate(Snap("output\n──────\ndo you want to proceed?"), "claude")
            .MatchedRuleId.ShouldBe("proceed");
    }

    [Fact]
    public void Dado_PromptBoxBody_Quando_Eval_Entao_SoUltimaLinhaConta()
    {
        var engine = Engine(("shell.toml", Shell));

        engine.Evaluate(Snap("output\n[Y/n]"), "anything").MatchedRuleId.ShouldBe("yn");
        engine.Evaluate(Snap("[Y/n]\ntyped already"), "anything")
            .MatchedRuleId.ShouldBeNull();
    }

    [Fact]
    public void Dado_RegiaoDesconhecida_Quando_Eval_Entao_Warning()
    {
        var engine = Engine(("shell.toml", """
            id = "shell"
            [[rules]]
            id = "r"
            state = "working"
            region = "somewhere_else"
            contains = ["x"]
            """));

        var v = engine.Evaluate(Snap("x"), "claude");
        v.MatchedRuleId.ShouldBeNull();
        v.Warnings.ShouldContain(w => w.Contains("unknown-region:somewhere_else"));
    }

    // ---------- resolution / precedence ----------

    [Fact]
    public void Dado_CliDesconhecido_Quando_Eval_Entao_FallbackShell()
    {
        var engine = Engine(("shell.toml", Shell), ("claude.toml", Claude));

        var v = engine.Evaluate(Snap("[Y/n]"), "vim");
        v.ManifestId.ShouldBe("shell");
        v.MatchedRuleId.ShouldBe("yn");
    }

    [Fact]
    public void Dado_Alias_Quando_Eval_Entao_ResolveManifesto()
    {
        var engine = Engine(("claude.toml", Claude));

        var v = engine.Evaluate(
            Snap("───\ndo you want to proceed?"), "claude-code");
        v.ManifestId.ShouldBe("claude");
        v.MatchedRuleId.ShouldBe("proceed");
    }

    [Fact]
    public void Dado_BinarioComPath_Quando_Eval_Entao_Normaliza()
    {
        AgentDetectionEngine.NormalizeCli("/usr/local/bin/claude").ShouldBe("claude");
        AgentDetectionEngine.NormalizeCli("codex.exe").ShouldBe("codex");
        AgentDetectionEngine.NormalizeCli(null).ShouldBeNull();
    }

    [Fact]
    public void Dado_MinEngineMaior_Quando_Load_Entao_Incompatible()
    {
        var engine = Engine(("claude.toml", """
            id = "claude"
            min_engine_version = 99
            [[rules]]
            id = "r"
            state = "working"
            contains = ["x"]
            """), ("shell.toml", Shell));

        var v = engine.Evaluate(Snap("[Y/n]"), "claude");
        v.ManifestId.ShouldBe("shell");
        engine.LoadWarnings.ShouldContain(w => w.Contains("incompatible"));
    }

    [Fact]
    public void Dado_TomlRuim_Quando_Load_Entao_WarningSemCrash()
    {
        var engine = Engine(("bad.toml", "id = [not valid"), ("shell.toml", Shell));

        var v = engine.Evaluate(Snap("[Y/n]"), "bad");
        v.MatchedRuleId.ShouldBe("yn");
        engine.LoadWarnings.ShouldContain(w => w.Contains("bad.toml"));
    }

    // ---------- overrides + reload ----------

    [Fact]
    public void Dado_OverrideExistente_Quando_Load_Entao_SombraBundled()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "claude.toml"), """
                id = "claude"
                [[rules]]
                id = "custom"
                state = "blocked"
                priority = 999
                region = "whole_recent"
                contains = ["custom-marker"]
                """);

            var engine = new AgentDetectionEngine(
                new AgentDetectionManifestLoader(
                    new Dictionary<string, string> { ["claude.toml"] = Claude },
                    overrideDir: dir.FullName));

            var v = engine.Evaluate(Snap("custom-marker"), "claude");
            v.MatchedRuleId.ShouldBe("custom");
            v.ManifestSource.ShouldBe("override");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dado_OverrideRuim_Quando_Load_Entao_BundledPrevaleceComWarning()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "claude.toml"), "id = [broken");

            var engine = new AgentDetectionEngine(
                new AgentDetectionManifestLoader(
                    new Dictionary<string, string> { ["claude.toml"] = Claude },
                    overrideDir: dir.FullName));

            var v = engine.Evaluate(
                Snap("───\ndo you want to proceed?"), "claude");
            v.MatchedRuleId.ShouldBe("proceed");
            v.ManifestSource.ShouldBe("bundled");
            engine.LoadWarnings.ShouldContain(w => w.Contains("rejected"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dado_NovoOverride_Quando_Reload_Entao_EngineMudaSemRestart()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var loader = new AgentDetectionManifestLoader(
                new Dictionary<string, string> { ["claude.toml"] = Claude },
                overrideDir: dir.FullName);
            var engine = new AgentDetectionEngine(loader);

            engine.Evaluate(Snap("brand-new-marker"), "claude")
                .MatchedRuleId.ShouldBeNull();

            File.WriteAllText(Path.Combine(dir.FullName, "claude.toml"), """
                id = "claude"
                [[rules]]
                id = "newrule"
                state = "blocked"
                priority = 999
                region = "whole_recent"
                contains = ["brand-new-marker"]
                """);

            var result = engine.Reload();
            result.CliCount.ShouldBe(1);
            result.OverrideCount.ShouldBe(1);

            var v = engine.Evaluate(Snap("brand-new-marker"), "claude");
            v.MatchedRuleId.ShouldBe("newrule");
            v.ManifestSource.ShouldBe("override");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ---------- explain ----------

    [Fact]
    public void Dado_EvalComPerdedores_Quando_Explain_Entao_FailedGatePorRegra()
    {
        var engine = Engine(("claude.toml", Claude));
        var v = engine.Evaluate(
            Snap("───\ndo you want to proceed?"), "claude");

        var matched = v.EvaluatedRules.Single(r => r.Id == "proceed");
        matched.Matched.ShouldBeTrue();
        var loser = v.EvaluatedRules.Single(r => r.Id == "interrupt");
        loser.Matched.ShouldBeFalse();
        loser.FailedGate.ShouldNotBeNull().ShouldContain("contains");
    }

    // ---------- bundled catalog (embedded resources) ----------

    [Fact]
    public void Dado_BundledReal_Quando_Load_Entao_CatalogoEmbutidoCarrega()
    {
        var engine = new AgentDetectionEngine(
            new AgentDetectionManifestLoader(overrideDir: null));

        // Claude bundled: permission form below a rule → blocked.
        var v = engine.Evaluate(
            Snap("output\n────────\ndo you want to proceed?\n  ❯ 1. Yes\n  2. No"),
            "claude");
        v.MatchedRuleId.ShouldBe("permission_form");
        v.ManifestSource.ShouldBe("bundled");

        // Unknown CLI falls back to the bundled shell manifest.
        var generic = engine.Evaluate(Snap("approve?"), "vim");
        generic.ManifestId.ShouldBe("shell");
        generic.MatchedRuleId.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_ExplainStore_Quando_Record_Entao_GetEDiscard()
    {
        var store = new AgentDetectionExplainStore();
        var dto = new AgentDetectionExplainDto(
            "term:x", AgentAttentionState.WaitingYou, "claude", "bundled",
            new EvaluatedRuleDto("proceed", 800, "after_last_horizontal_rule", true, null),
            [], ["visible_blocker"], [], "abc123", DateTime.UtcNow);

        store.Get("term:x").ShouldBeNull();
        store.Record("term:x", dto);
        store.Get("term:x").ShouldBe(dto);
        store.Discard("term:x");
        store.Get("term:x").ShouldBeNull();
    }
}
