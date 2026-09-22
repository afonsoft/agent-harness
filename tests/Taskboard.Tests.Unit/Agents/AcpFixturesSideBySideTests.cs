using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness: fixtures separadas v1/v2
/// (tests/fixtures/acp/&lt;versão&gt;) parseadas lado a lado — a mesma sequência
/// semântica deve normalizar para os kinds esperados em cada dialeto.
/// </summary>
public sealed class AcpFixturesSideBySideTests
{
    private static string FixturePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("tests/fixtures não encontrado acima de " + AppContext.BaseDirectory);
        return Path.Join(dir!.FullName, "tests", "fixtures", relative);
    }

    private static List<AcpProtocolParser.Parsed> ParseFixture(string path, int version) =>
        File.ReadLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => AcpProtocolParser.Parse(l, version))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    [Fact]
    public void Dado_FixtureV1_Quando_Parse_Entao_SequenciaNormalizadaV1()
    {
        var parsed = ParseFixture(FixturePath("acp/v1/session-updates.jsonl"), 1);

        parsed.Select(p => p.Kind).ShouldBe([
            AgentEventKinds.Message,
            AgentEventKinds.Thought,
            AgentEventKinds.ToolCall,
            AgentEventKinds.ToolOutput,
            AgentEventKinds.Plan,
            AgentEventKinds.Metric,
            AgentEventKinds.Permission
        ]);
        // Envelope v1: nenhum campo upsert v2 preenchido.
        parsed.ShouldAllBe(p => p.MessageId == null && p.PlanId == null && p.PatchOp == null);
    }

    [Fact]
    public void Dado_FixtureV2_Quando_Parse_Entao_SequenciaNormalizadaV2ComUpsert()
    {
        var parsed = ParseFixture(FixturePath("acp/v2/session-updates.jsonl"), 2);

        parsed.Select(p => p.Kind).ShouldBe([
            AgentEventKinds.Message,
            AgentEventKinds.Thought,
            // v2 não tem tool_call — a primeira tool_call_update cria a entidade
            // (o cliente reclassifica como tool_call no dispatch).
            AgentEventKinds.ToolOutput,
            AgentEventKinds.ToolOutput,
            AgentEventKinds.Plan,
            AgentEventKinds.Metric,
            AgentEventKinds.State,
            AgentEventKinds.Permission
        ]);

        parsed[0].MessageId.ShouldBe("m-1");
        parsed[0].PatchOp.ShouldBe("append");
        parsed[2].ToolCallId.ShouldBe("tc-1");
        parsed[2].EntityKind.ShouldBe("tool_call");
        parsed[4].PlanId.ShouldBe("plan-1");
        parsed[6].PayloadJson!.ShouldContain("end_turn");
    }

    [Fact]
    public void Dado_FixturesV1V2_Quando_Comparadas_Entao_MesmaSemantica()
    {
        // Os primeiros 6 eventos das duas fixtures carregam a mesma semântica —
        // o que muda é apenas o wire format.
        var v1 = ParseFixture(FixturePath("acp/v1/session-updates.jsonl"), 1);
        var v2 = ParseFixture(FixturePath("acp/v2/session-updates.jsonl"), 2);

        v1[0].Content.ShouldBe(v2[0].Content); // "olá"
        v1[1].Content.ShouldBe(v2[1].Content); // "pensando"
        v1[2].ToolCallId.ShouldBe(v2[2].ToolCallId);
        v1[4].Kind.ShouldBe(v2[4].Kind); // plan
        v1[5].Kind.ShouldBe(v2[5].Kind); // metric
    }

    [Fact]
    public void Dado_FixtureBatchV2_Quando_ParsePorElemento_Entao_TresEntradas()
    {
        var lines = File.ReadAllLines(FixturePath("acp/v2/batch.jsonl"))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        lines.Count.ShouldBe(2);
        lines[0].TrimStart().ShouldStartWith("["); // batch NDJSON
        lines[1].TrimStart().ShouldNotStartWith("[");

        var parsed = AcpProtocolParser.Parse(lines[1], 2)!;
        parsed.Kind.ShouldBe(AgentEventKinds.State);
    }
}
