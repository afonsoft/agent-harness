using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Blazor.Components.AiChat;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260928-ai-code-ux-simplify RF-001/RF-002: o rail persistente de
/// threads — lista ordenada, estado ativo, delete inline-confirm, e o botão
/// "+" desabilitado quando não há CLI elegível.
/// </summary>
public class ThreadRailTests : BunitContext
{
    public ThreadRailTests()
    {
        Services.AddBlazorBootstrap();
    }

    private static AiChatThreadDto Thread(string id, string title, int minutesAgo = 0) =>
        new(id, title, "model-x", "none", "read-only", "idle",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-minutesAgo), 1,
            Mode: "agent", AgentType: "Claude");

    [Fact]
    public void Dado_Threads_Quando_Render_Entao_ListaNaOrdemComAtiva()
    {
        var threads = new[] { Thread("t1", "Primeira"), Thread("t2", "Segunda", 5) };

        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, threads)
            .Add(p => p.ActiveThreadId, "t2"));

        var items = cut.FindAll(".ai-chat-thread-item");
        items.Count.ShouldBe(2);
        items[0].TextContent.ShouldContain("Primeira");
        (items[1].ClassName ?? string.Empty).ShouldContain("active");
        items[1].TextContent.ShouldContain("Segunda");
    }

    [Fact]
    public void Dado_SemCliElegivel_Quando_Render_Entao_NewDesabilitado()
    {
        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, Array.Empty<AiChatThreadDto>())
            .Add(p => p.HasEligibleAgent, false));

        var newButton = cut.Find(".ai-chat-rail-new");

        newButton.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Dado_CliqueNoNew_Quando_Habilitado_Entao_CallbackDispara()
    {
        var fired = false;
        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, Array.Empty<AiChatThreadDto>())
            .Add(p => p.HasEligibleAgent, true)
            .Add(p => p.OnNew, () => fired = true));

        cut.Find(".ai-chat-rail-new").Click();

        fired.ShouldBeTrue();
    }

    [Fact]
    public void Dado_CliqueNaThread_Quando_Item_Entao_OnSelectComThread()
    {
        AiChatThreadDto? selected = null;
        var threads = new[] { Thread("t1", "Primeira") };
        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, threads)
            .Add(p => p.OnSelect, (AiChatThreadDto t) => selected = t));

        cut.Find(".ai-chat-thread-btn").Click();

        selected?.Id.ShouldBe("t1");
    }

    [Fact]
    public void Dado_Delete_Quando_Confirma_Entao_OnDeleteDispara()
    {
        AiChatThreadDto? deleted = null;
        var threads = new[] { Thread("t1", "Primeira") };
        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, threads)
            .Add(p => p.OnDelete, (AiChatThreadDto t) => deleted = t));

        cut.Find(".ai-chat-thread-delete").Click();
        cut.Find(".ai-chat-thread-confirm .btn-danger").Click();

        deleted?.Id.ShouldBe("t1");
    }

    [Fact]
    public void Dado_Colapsado_Quando_Render_Entao_IconesComTooltip()
    {
        var threads = new[] { Thread("t1", "Primeira") };

        var cut = Render<ThreadRail>(parameters => parameters
            .Add(p => p.Threads, threads)
            .Add(p => p.Collapsed, true));

        (cut.Find(".ai-chat-rail").ClassName ?? string.Empty).ShouldContain("is-collapsed");
        var iconBtn = cut.Find(".ai-chat-rail-iconbtn");
        iconBtn.GetAttribute("title").ShouldBe("Primeira");
    }
}
