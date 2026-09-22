using Shouldly;
using Taskboard.Domain.Shared.Configuration;
using Xunit;

namespace Taskboard.Tests.Unit.Configuration;

/// <summary>
/// SPEC-20260922-harness-home-rename RF-001: HARNESS_* canonical,
/// TASKBOARD_* deprecated fallback.
/// </summary>
public class HarnessEnvTests
{
    private const string Canonical = "HARNESS_TEST_HARNESSENV";
    private const string Legacy = "TASKBOARD_TEST_HARNESSENV";

    [Fact]
    public void Dado_ApenasCanonical_Quando_Get_Entao_RetornaValor()
    {
        Environment.SetEnvironmentVariable(Canonical, " novo ");
        Environment.SetEnvironmentVariable(Legacy, null);
        try
        {
            HarnessEnv.Get(Canonical).ShouldBe("novo");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Canonical, null);
        }
    }

    [Fact]
    public void Dado_ApenasLegado_Quando_Get_Entao_FallbackComCallback()
    {
        Environment.SetEnvironmentVariable(Canonical, null);
        Environment.SetEnvironmentVariable(Legacy, "legado");
        string? warned = null;
        try
        {
            HarnessEnv.Get(Canonical, name => warned = name).ShouldBe("legado");
            warned.ShouldBe(Legacy);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Legacy, null);
        }
    }

    [Fact]
    public void Dado_Ambos_Quando_Get_Entao_CanonicalVence()
    {
        Environment.SetEnvironmentVariable(Canonical, "novo");
        Environment.SetEnvironmentVariable(Legacy, "legado");
        try
        {
            HarnessEnv.Get(Canonical).ShouldBe("novo");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Canonical, null);
            Environment.SetEnvironmentVariable(Legacy, null);
        }
    }

    [Fact]
    public void Dado_Nenhum_Quando_Get_Entao_NullEIsSetFalse()
    {
        Environment.SetEnvironmentVariable(Canonical, null);
        Environment.SetEnvironmentVariable(Legacy, null);

        HarnessEnv.Get(Canonical).ShouldBeNull();
        HarnessEnv.IsSet(Canonical).ShouldBeFalse();
    }
}
