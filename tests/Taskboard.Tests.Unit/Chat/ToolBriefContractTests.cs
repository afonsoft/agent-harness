using Shouldly;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Chat.Tools.Board;
using Taskboard.Integrations.Chat.Tools.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261022-sdd-worker-brief RF-006: tool descriptions are the
/// prompt contract — each delegation/automation tool must teach the
/// self-contained-brief shape.</summary>
public sealed class ToolBriefContractTests
{
    private const string ContractPhrase = "self-contained brief";

    [Theory]
    [InlineData(typeof(DelegateTaskTool))]
    [InlineData(typeof(BoardDelegateIssueTool))]
    [InlineData(typeof(DelegatePlanTool))]
    [InlineData(typeof(AutomationCreateTool))]
    public void Dado_ToolDeDelegacao_Quando_Description_Entao_EnsinaOBrief(System.Type type)
    {
        var ctors = type.GetConstructors();
        var args = ctors[0].GetParameters().Select(p => p.ParameterType.IsValueType
            ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        var tool = (Taskboard.Application.Contracts.Chat.IChatTool)
            System.Activator.CreateInstance(type, args)!;

        tool.Description.ShouldContain(ContractPhrase, Case.Insensitive);
        tool.Description.ShouldContain("acceptance", Case.Insensitive);
    }

    [Fact]
    public void Dado_Planner_Quando_BuildPlannerPrompt_Entao_AncoraSpec()
    {
        var prompt = Taskboard.Integrations.Delegation.DelegationDispatcherService
            .BuildPlannerPrompt("goal");

        prompt.ShouldContain("spec_ref");
        prompt.ShouldContain("\"ac\"");
    }

    [Fact]
    public void Dado_DelegacaoHabilitada_Quando_OrchestrationSection_Entao_ContemRotasSDD()
    {
        // AC-07: com ferramentas de delegação, o system prompt ganha o bloco.
        var section = Taskboard.Application.Chat.ChatService.OrchestrationSection(hasDelegationTools: true);

        section.ShouldContain("SDD orchestration");
        section.ShouldContain("write-specs");
        section.ShouldContain("gap-analysis");
        section.ShouldContain("self-contained brief");
    }

    [Fact]
    public void Dado_DelegacaoDesabilitada_Quando_OrchestrationSection_Entao_Vazia()
    {
        // AC-07: sem ferramentas de delegação, o bloco não aparece.
        Taskboard.Application.Chat.ChatService.OrchestrationSection(hasDelegationTools: false)
            .ShouldBeEmpty();
    }
}