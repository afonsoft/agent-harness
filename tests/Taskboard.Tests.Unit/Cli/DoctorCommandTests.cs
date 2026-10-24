using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using CliProgram = Taskboard.Cli.Program;

namespace Taskboard.Tests.Unit.Cli;

/// <summary>
/// SPEC-20261023 RF-C02: render do relatório de preflight no taskctl —
/// tabela status/name/message + linha de veredito; exit 0 ready|warning,
/// 2 blocked.
/// </summary>
public class DoctorCommandTests
{
    private static async Task<(int Exit, string Out)> RenderAsync(string payload, bool json = false)
    {
        var node = JsonNode.Parse(payload);
        var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var exit = await CliProgram.WritePreflightReportAsync(node, json);
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private const string ReadyReport = """
        {"verdict":"ready","checks":[
          {"name":"cli:claude","status":0,"message":"Claude v1.2.3 · authenticated"},
          {"name":"mcp","status":0,"message":"2 MCP server(s) reachable"}
        ]}
        """;

    private const string BlockedReport = """
        {"verdict":"blocked","checks":[
          {"name":"chat-provider","status":2,"message":"no chat provider API key configured","nextAction":"set ANTHROPIC_API_KEY"}
        ]}
        """;

    [Fact]
    public async Task Dado_VerdictReady_Quando_Render_Entao_Exit0ComVerdict()
    {
        var (exit, output) = await RenderAsync(ReadyReport);

        exit.ShouldBe(0);
        output.ShouldContain("verdict: ready");
        output.ShouldContain("cli:claude");
        output.ShouldContain("authenticated");
    }

    [Fact]
    public async Task Dado_VerdictWarning_Quando_Render_Entao_Exit0()
    {
        var (exit, _) = await RenderAsync("""{"verdict":"warning","checks":[]}""");

        exit.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_VerdictBlocked_Quando_Render_Entao_Exit2ComNextAction()
    {
        var (exit, output) = await RenderAsync(BlockedReport);

        exit.ShouldBe(2);
        output.ShouldContain("verdict: blocked");
        output.ShouldContain("set ANTHROPIC_API_KEY");
    }

    [Fact]
    public async Task Dado_Json_Quando_Render_Entao_SaidaBrutaEExitPorVerdict()
    {
        var (exit, output) = await RenderAsync(BlockedReport, json: true);

        exit.ShouldBe(2);
        var roundTrip = JsonNode.Parse(output) as JsonObject;
        roundTrip.ShouldNotBeNull();
        roundTrip["verdict"]!.GetValue<string>().ShouldBe("blocked");
    }

    [Fact]
    public async Task Dado_StatusString_Quando_Render_Entao_Aceito()
    {
        var (exit, output) = await RenderAsync(
            """{"verdict":"warning","checks":[{"name":"mcp","status":"warning","message":"ctx7 down"}]}""");

        exit.ShouldBe(0);
        output.ShouldContain("warning");
        output.ShouldContain("ctx7 down");
    }
}
