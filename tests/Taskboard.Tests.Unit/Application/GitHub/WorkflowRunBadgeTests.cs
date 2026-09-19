using Shouldly;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Application.GitHub;

public class WorkflowRunBadgeTests
{
    [Theory]
    [InlineData("in_progress")]
    [InlineData("queued")]
    [InlineData("requested")]
    [InlineData("waiting")]
    [InlineData("pending")]
    public void Dado_StatusVivo_Quando_IsLive_Entao_True(string status)
    {
        WorkflowRunBadge.IsLive(status).ShouldBeTrue();
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("success")]
    [InlineData(null)]
    public void Dado_StatusNaoVivo_Quando_IsLive_Entao_False(string? status)
    {
        WorkflowRunBadge.IsLive(status).ShouldBeFalse();
    }

    [Fact]
    public void Dado_RunSucesso_Quando_CssClass_Entao_Verde()
    {
        WorkflowRunBadge.CssClass("completed", "success").ShouldContain("text-bg-success");
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("timed_out")]
    [InlineData("startup_failure")]
    [InlineData("action_required")]
    public void Dado_RunFalho_Quando_CssClass_Entao_Vermelho(string conclusion)
    {
        WorkflowRunBadge.CssClass("completed", conclusion).ShouldContain("text-bg-danger");
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    [InlineData("neutral")]
    [InlineData("stale")]
    public void Dado_RunNeutro_Quando_CssClass_Entao_Cinza(string conclusion)
    {
        WorkflowRunBadge.CssClass("completed", conclusion).ShouldContain("text-bg-secondary");
    }

    [Fact]
    public void Dado_RunVivo_Quando_CssClass_Entao_AmareloAnimado()
    {
        var css = WorkflowRunBadge.CssClass("in_progress", null);

        css.ShouldContain("text-bg-warning");
        css.ShouldContain("workflow-badge-live");
    }

    [Fact]
    public void Dado_RunVivo_Quando_Label_Entao_ExibeStatus()
    {
        WorkflowRunBadge.Label("queued", null).ShouldBe("queued");
    }

    [Fact]
    public void Dado_RunCompleto_Quando_Label_Entao_ExibeConclusao()
    {
        WorkflowRunBadge.Label("completed", "failure").ShouldBe("failure");
    }

    [Fact]
    public void Dado_StatusDesconhecido_Quando_Label_Entao_Unknown()
    {
        WorkflowRunBadge.Label(null, null).ShouldBe("unknown");
    }
}
