using System.Collections.Generic;
using Shouldly;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Xunit;

namespace Taskboard.Tests.Unit.EntityFrameworkCore;

public class JsonValueConverterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void Dado_PayloadInvalido_Quando_ListStringConvertFromProvider_Entao_RetornaListaVazia(string payload)
    {
        var converter = new ListStringJsonValueConverter();

        var result = converter.ConvertFromProvider(payload);

        result.ShouldBeOfType<List<string>>().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_PayloadNulo_Quando_ListStringConvertFromProvider_Entao_PassaNuloSemInvocar()
    {
        // EF Core sanitizes provider nulls before invoking the converter.
        var converter = new ListStringJsonValueConverter();

        converter.ConvertFromProvider(null).ShouldBeNull();
    }

    [Fact]
    public void Dado_PayloadValido_Quando_ListStringConvertFromProvider_Entao_RetornaItens()
    {
        var converter = new ListStringJsonValueConverter();

        var result = converter.ConvertFromProvider("[\"a\",\"b\"]");

        result.ShouldBeOfType<List<string>>().ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Dado_PayloadVazio_Quando_ReadOnlyListConvertFromProvider_Entao_RetornaListaVazia()
    {
        var converter = new ReadOnlyListStringJsonValueConverter();

        var result = converter.ConvertFromProvider("");

        result.ShouldBeAssignableTo<IReadOnlyList<string>>().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_PayloadVazio_Quando_NullableJsonConvertFromProvider_Entao_RetornaNulo()
    {
        var converter = new NullableJsonValueConverter<Payload>();

        converter.ConvertFromProvider("").ShouldBeNull();
        converter.ConvertFromProvider("lixo").ShouldBeNull();
    }

    [Fact]
    public void Dado_PayloadValido_Quando_NullableJsonConvertFromProvider_Entao_Desserializa()
    {
        var converter = new NullableJsonValueConverter<Payload>();

        var result = converter.ConvertFromProvider("{\"name\":\"x\"}");

        result.ShouldBeOfType<Payload>().Name.ShouldBe("x");
    }

    [Fact]
    public void Dado_PayloadVazio_Quando_JsonConvertFromProvider_Entao_RetornaNulo()
    {
        var converter = new JsonValueConverter<Payload>();

        converter.ConvertFromProvider("").ShouldBeNull();
    }

    private sealed class Payload
    {
        public string? Name { get; set; }
    }
}
