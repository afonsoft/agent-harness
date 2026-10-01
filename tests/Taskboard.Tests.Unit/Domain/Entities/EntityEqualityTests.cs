using Taskboard.Domain.Entities;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Domain.Entities;

/// <summary>
/// B-10 (SPEC-20261001-pr-review-backlog-fixes): <c>==</c>/<c>!=</c> de
/// <see cref="Entity{TKey}"/> devem concordar com <see cref="Entity{TKey}.Equals(object)"/>
/// — igualdade por Id, não por referência.
/// </summary>
public class EntityEqualityTests
{
    private sealed class FakeEntityCtor : Entity<Guid>
    {
        public FakeEntityCtor(Guid id)
        {
            Id = id;
        }
    }

    [Fact]
    public void Dado_DuasInstanciasComMesmoId_Quando_Comparar_Entao_OperadoresConcordamComEquals()
    {
        var id = Guid.NewGuid();
        var a = new FakeEntityCtor(id);
        var b = new FakeEntityCtor(id);

        (a == b).ShouldBeTrue("mesmo Id deve ser igual por ==");
        (a != b).ShouldBeFalse();
        a.Equals(b).ShouldBeTrue();
        (a.GetHashCode() == b.GetHashCode()).ShouldBeTrue();
    }

    [Fact]
    public void Dado_IdsDiferentes_Quando_Comparar_Entao_OperadoresDivergem()
    {
        var a = new FakeEntityCtor(Guid.NewGuid());
        var b = new FakeEntityCtor(Guid.NewGuid());

        (a == b).ShouldBeFalse();
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Dado_Null_Quando_Comparar_Entao_OperadoresRespeitamNull()
    {
        var a = new FakeEntityCtor(Guid.NewGuid());

        (a == null).ShouldBeFalse();
        (a != null).ShouldBeTrue();
        ((FakeEntityCtor?)null == null).ShouldBeTrue();
        ((FakeEntityCtor?)null != null).ShouldBeFalse();
    }

    [Fact]
    public void Dado_EntidadesComIdDefault_Quando_Comparar_Entao_OperadoresConcordamComEquals()
    {
        var a = new FakeEntityCtor(default);
        var b = new FakeEntityCtor(default);

        // TKey struct nunca é null — Equals compara os Ids default; o ponto da
        // correção é que == concorde com Equals, qualquer que seja o veredicto.
        (a == b).ShouldBe(a.Equals(b));
        (a != b).ShouldBe(!a.Equals(b));
    }
}
