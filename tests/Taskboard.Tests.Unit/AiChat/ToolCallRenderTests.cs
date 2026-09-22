using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260921-ai-code-chat-ux RF-001/RF-006: dispatch de renderers por
/// tool kind, merge tool_call+tool_call_update por toolCallId e agregação
/// "changes" de edições consecutivas.
/// </summary>
public class ToolCallRenderTests
{
    [Fact]
    public void Dado_ToolCallEditComDiff_Quando_Parse_Entao_ModeloComPathEOldNew()
    {
        var call = """
        {
            "sessionUpdate": "tool_call", "toolCallId": "c1", "title": "Edit src/Foo.cs",
            "kind": "edit", "status": "in_progress",
            "locations": [{ "path": "src/Foo.cs" }],
            "content": [{ "type": "diff", "path": "src/Foo.cs", "oldText": "a\nb", "newText": "a\nc" }]
        }
        """;

        var model = ToolCallRender.Parse(call, []);

        model.Kind.ShouldBe("edit");
        model.Path.ShouldBe("src/Foo.cs");
        model.OldText.ShouldBe("a\nb");
        model.NewText.ShouldBe("a\nc");
        model.AddedLines.ShouldBe(1);
        model.RemovedLines.ShouldBe(1);
    }

    [Fact]
    public void Dado_ToolCallExecuteComOutput_Quando_Parse_Entao_KindExecuteComExitCode()
    {
        var call = """
        {
            "sessionUpdate": "tool_call", "toolCallId": "c2", "title": "dotnet test",
            "kind": "execute", "status": "in_progress"
        }
        """;
        var update = """
        {
            "sessionUpdate": "tool_call_update", "toolCallId": "c2",
            "status": "completed", "rawOutput": "all tests passed", "exitCode": 0
        }
        """;

        var model = ToolCallRender.Parse(call, [update]);

        model.Kind.ShouldBe("execute");
        model.Status.ShouldBe("completed");
        model.Output.ShouldBe("all tests passed");
        model.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Dado_ToolCallRead_Quando_Parse_Entao_KindReadComPreview()
    {
        var call = """
        {
            "sessionUpdate": "tool_call", "toolCallId": "c3", "title": "Read README.md",
            "kind": "read", "status": "completed",
            "locations": [{ "path": "README.md" }],
            "rawOutput": "line1\nline2\nline3"
        }
        """;

        var model = ToolCallRender.Parse(call, []);

        model.Kind.ShouldBe("read");
        model.Path.ShouldBe("README.md");
        model.Output.ShouldBe("line1\nline2\nline3");
    }

    [Fact]
    public void Dado_KindDesconhecido_Quando_Parse_Entao_Other()
    {
        var call = """
        {
            "sessionUpdate": "tool_call", "toolCallId": "c4", "title": "fetch url",
            "kind": "fetch", "status": "completed"
        }
        """;

        ToolCallRender.Parse(call, []).Kind.ShouldBe("other");
    }

    [Fact]
    public void Dado_UpdateComDiffContent_Quando_Parse_Entao_DiffPropaga()
    {
        var call = """
        { "sessionUpdate": "tool_call", "toolCallId": "c5", "title": "write", "kind": "edit" }
        """;
        var update = """
        {
            "sessionUpdate": "tool_call_update", "toolCallId": "c5", "status": "completed",
            "content": [{ "type": "diff", "path": "x.cs", "oldText": "one", "newText": "one\ntwo" }]
        }
        """;

        var model = ToolCallRender.Parse(call, [update]);

        model.Path.ShouldBe("x.cs");
        model.NewText.ShouldBe("one\ntwo");
        model.Status.ShouldBe("completed");
    }

    [Fact]
    public void Dado_ExitStatusAninhado_Quando_Parse_Entao_ExitCodeExtraido()
    {
        var update = """
        {
            "sessionUpdate": "tool_call_update", "toolCallId": "c6",
            "status": "completed", "exitStatus": { "exitCode": 3 }
        }
        """;

        ToolCallRender.Parse(null, [update]).ExitCode.ShouldBe(3);
    }
}

public class ToolCallGrouperTests
{
    private static AiChatEventDto ToolCall(string id, string payload) =>
        new(id, "t", "assistant", "title", DateTime.UtcNow, "tool_call", payload);

    private static AiChatEventDto ToolOutput(string id, string payload) =>
        new(id, "t", "assistant", "out", DateTime.UtcNow, "tool_output", payload);

    private static AiChatEventDto Msg(string id, string role = "user") =>
        new(id, "t", role, "hello", DateTime.UtcNow, "message");

    [Fact]
    public void Dado_ToolCallEUpdateMesmoId_Quando_Build_Entao_MesclaNumModelo()
    {
        var items = ToolCallGrouper.Build(
        [
            ToolCall("e1", """{ "toolCallId": "c1", "kind": "execute", "title": "run", "status": "in_progress" }"""),
            ToolOutput("e2", """{ "toolCallId": "c1", "status": "completed", "rawOutput": "done" }"""),
        ]);

        items.Count.ShouldBe(1);
        var tool = items[0].Tool.ShouldNotBeNull();
        tool.Status.ShouldBe("completed");
        tool.Output.ShouldBe("done");
    }

    [Fact]
    public void Dado_EditsConsecutivos_Quando_Build_Entao_GrupoChanges()
    {
        var events = new List<AiChatEventDto>
        {
            ToolCall("e1", """{ "toolCallId": "c1", "kind": "edit", "content": [{ "type": "diff", "path": "a.cs", "oldText": "x", "newText": "x\ny" }] }"""),
            ToolCall("e2", """{ "toolCallId": "c2", "kind": "edit", "content": [{ "type": "diff", "path": "b.cs", "oldText": "p\nq", "newText": "p" }] }"""),
            ToolCall("e3", """{ "toolCallId": "c3", "kind": "edit", "content": [{ "type": "diff", "path": "c.cs", "oldText": "", "newText": "n" }] }"""),
        };

        var items = ToolCallGrouper.Build(events);

        items.Count.ShouldBe(1);
        var group = items[0].Group.ShouldNotBeNull();
        group.Count.ShouldBe(3);
        group[0].Path.ShouldBe("a.cs");
        group[1].RemovedLines.ShouldBe(1);
    }

    [Fact]
    public void Dado_EditsSeparadosPorMensagem_Quando_Build_Entao_CardsIndividuais()
    {
        var items = ToolCallGrouper.Build(
        [
            ToolCall("e1", """{ "toolCallId": "c1", "kind": "edit" }"""),
            Msg("m1", "assistant"),
            ToolCall("e2", """{ "toolCallId": "c2", "kind": "edit" }"""),
        ]);

        items.Count.ShouldBe(3);
        items[0].Tool.ShouldNotBeNull();
        items[1].Event.ShouldNotBeNull();
        items[2].Tool.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_ToolOutputSemCall_Quando_Build_Entao_CardGenerico()
    {
        var items = ToolCallGrouper.Build(
        [
            ToolOutput("e1", """{ "status": "completed", "rawOutput": "orphan" }"""),
        ]);

        items.Count.ShouldBe(1);
        var tool = items[0].Tool.ShouldNotBeNull();
        tool.Kind.ShouldBe("other");
        tool.Output.ShouldBe("orphan");
    }

    [Fact]
    public void Dado_EventosDeMensagem_Quando_Build_Entao_PassamInalterados()
    {
        var items = ToolCallGrouper.Build([Msg("m1"), Msg("m2", "assistant")]);

        items.Count.ShouldBe(2);
        items.ShouldAllBe(i => i.Event != null && i.Tool == null);
    }

    [Fact]
    public void Dado_ExecEntreEdits_Quando_Build_Entao_QuebraGrupo()
    {
        var items = ToolCallGrouper.Build(
        [
            ToolCall("e1", """{ "toolCallId": "c1", "kind": "edit" }"""),
            ToolCall("e2", """{ "toolCallId": "c2", "kind": "execute" }"""),
            ToolCall("e3", """{ "toolCallId": "c3", "kind": "edit" }"""),
        ]);

        items.Count.ShouldBe(3);
        items[0].Group.ShouldBeNull();
        items[1].Tool.ShouldNotBeNull();
        items[2].Group.ShouldBeNull();
    }

    [Fact]
    public void Dado_UsageUpdatePlano_Quando_TryParse_Entao_ExtraiUsedSizeCost()
    {
        var usage = ContextUsage.TryParse("""{ "used": 50000, "size": 200000, "cost": 0.0123 }""");

        usage.ShouldNotBeNull();
        usage!.Used.ShouldBe(50000);
        usage.Size.ShouldBe(200000);
        usage.Cost!.Value.ShouldBe(0.0123, 0.0001);
        usage.Ratio!.Value.ShouldBe(0.25, 0.001);
    }

    [Fact]
    public void Dado_UsageSemSize_Quando_TryParse_Entao_RatioNull()
    {
        var usage = ContextUsage.TryParse("""{ "used": 100 }""");

        usage.ShouldNotBeNull();
        usage!.Ratio.ShouldBeNull();
    }

    [Fact]
    public void Dado_PayloadInvalido_Quando_TryParse_Entao_Null()
    {
        ContextUsage.TryParse("not json").ShouldBeNull();
        ContextUsage.TryParse("""{ "other": 1 }""").ShouldBeNull();
        ContextUsage.TryParse(null).ShouldBeNull();
    }
}
