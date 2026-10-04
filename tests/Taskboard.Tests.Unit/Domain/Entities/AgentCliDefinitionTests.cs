using Shouldly;
using Taskboard;
using Taskboard.Domain.Entities;
using Xunit;

namespace Taskboard.Tests.Unit.Domain.Entities;

/// <summary>SPEC-20260928-ai-code-generic-cli RF-002: validação da entidade persistida.</summary>
public class AgentCliDefinitionTests
{
    [Fact]
    public void Dado_DadosValidos_Quando_Create_Entao_IdCustomSlug()
    {
        var def = AgentCliDefinition.Create("My CLI", "mycli", "--acp {model}", "acp");

        def.Id.ShouldBe("custom-my-cli");
        def.DisplayName.ShouldBe("My CLI");
        def.Transport.ShouldBe("acp");
        def.Enabled.ShouldBeTrue();
        def.VersionArgs.ShouldBe("--version");
    }

    [Fact]
    public void Dado_SemArgs_Quando_Create_Entao_ArgsTemplateVazio()
    {
        var def = AgentCliDefinition.Create("Bare", "bare-cli", string.Empty, "pty");

        def.ArgsTemplate.ShouldBe(string.Empty);
        def.BuildArgs().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Dado_DisplayNameVazio_Quando_Create_Entao_InvalidValue(string name)
    {
        var ex = Should.Throw<DomainException>(
            () => AgentCliDefinition.Create(name, "cli", "", "pty"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_ExecutableVazio_Quando_Create_Entao_InvalidValue()
    {
        var ex = Should.Throw<DomainException>(
            () => AgentCliDefinition.Create("x", " ", "", "pty"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_TransporteInvalido_Quando_Create_Entao_InvalidValue()
    {
        var ex = Should.Throw<DomainException>(
            () => AgentCliDefinition.Create("x", "cli", "", "websocket"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_TransporteMaiusculo_Quando_Create_Entao_Normalizado()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "PTY");

        def.Transport.ShouldBe("pty");
    }

    [Fact]
    public void Dado_NomeSoSimbolos_Quando_SlugFor_Entao_InvalidValue()
    {
        var ex = Should.Throw<DomainException>(() => AgentCliDefinition.SlugFor("!!!"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_NomeLongo_Quando_SlugFor_Entao_TruncadoEm48()
    {
        var slug = AgentCliDefinition.SlugFor("agent " + new string('x', 100));

        slug.Length.ShouldBeLessThanOrEqualTo(48);
        slug.ShouldBe("agent-" + new string('x', 42));
    }

    [Fact]
    public void Dado_DefComModelFlag_Quando_BuildArgs_Entao_DelegaAoTemplate()
    {
        var def = AgentCliDefinition.Create("x", "cli", "--acp", "pty", modelFlag: "--model");

        def.BuildArgs(model: "sonnet").ShouldBe(["--acp", "--model", "sonnet"]);
    }

    [Fact]
    public void Dado_DefExistente_Quando_Update_Entao_CamposAtualizados()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "pty");

        def.Update("x", "cli2", "-v", "acp", null, "-V", enabled: false);

        def.Executable.ShouldBe("cli2");
        def.ArgsTemplate.ShouldBe("-v");
        def.Transport.ShouldBe("acp");
        def.VersionArgs.ShouldBe("-V");
        def.Enabled.ShouldBeFalse();
        def.UpdatedAt.ShouldBeGreaterThanOrEqualTo(def.CreatedAt);
    }

    [Fact]
    public void Dado_PromptDeliveryStdin_Quando_Create_Entao_DeliversViaStdin()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "pty", promptDelivery: "stdin");

        def.PromptDelivery.ShouldBe("stdin");
        def.DeliversPromptViaStdin.ShouldBeTrue();
    }

    [Theory]
    [InlineData("acp")]
    [InlineData("pty")]
    [InlineData("pipe")]
    public void Dado_PromptDeliveryInvalido_Quando_Create_Entao_InvalidValue(string delivery)
    {
        var ex = Should.Throw<DomainException>(
            () => AgentCliDefinition.Create("x", "cli", "", "pty", promptDelivery: delivery));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_ModelListArgs_Quando_Create_Entao_PersistidoTrimado()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "pty", modelListArgs: " models list ");

        def.ModelListArgs.ShouldBe("models list");
    }

    [Fact]
    public void Dado_UpdateSemNovosCampos_Quando_Update_Entao_ModelListArgsPreservado()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "pty", modelListArgs: "models");

        def.Update("x", "cli", "", "pty", null, null, enabled: true);

        def.PromptDelivery.ShouldBe("argv");
        def.ModelListArgs.ShouldBe("models");
    }

    [Fact]
    public void Dado_UpdateComDelivery_Quando_Update_Entao_SubstituiCampos()
    {
        var def = AgentCliDefinition.Create("x", "cli", "", "pty", modelListArgs: "models");

        def.Update("x", "cli", "", "pty", null, null, enabled: true, promptDelivery: "stdin");

        def.PromptDelivery.ShouldBe("stdin");
        def.ModelListArgs.ShouldBeNull("o Update trata os dois campos como um bloco — só delivery zera a lista");
    }
}
