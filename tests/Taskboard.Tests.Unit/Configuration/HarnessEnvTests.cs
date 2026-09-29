using Shouldly;
using Taskboard.Domain.Shared.Configuration;
using Xunit;

namespace Taskboard.Tests.Unit.Configuration;

/// <summary>
/// SPEC-20260928-taskboard-env-fallback-removal RF-001: only canonical
/// <c>HARNESS_*</c> names are read; <c>TASKBOARD_*</c> is ignored.
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
    public void Dado_ApenasLegado_Quando_Get_Entao_Ignorado()
    {
        // Breaking change (SPEC-20260928): the legacy TASKBOARD_* name is no
        // longer read — a host that only sets it gets the canonical default.
        Environment.SetEnvironmentVariable(Canonical, null);
        Environment.SetEnvironmentVariable(Legacy, "legado");
        try
        {
            HarnessEnv.Get(Canonical).ShouldBeNull();
            HarnessEnv.IsSet(Canonical).ShouldBeFalse();
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
