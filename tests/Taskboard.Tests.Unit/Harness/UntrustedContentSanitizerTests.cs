using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261023 RF-B01: sanitizer único de conteúdo externo — remove
/// zero-width/bidi chars e sinaliza runs homoglyph suspeitos antes do texto
/// chegar ao wire do modelo.
/// </summary>
public class UntrustedContentSanitizerTests
{
    [Theory]
    // Zero-width: ZWSP, ZWNJ, ZWJ, WORD JOINER, LRM, RLM, BOM, invisible operators
    [InlineData("he​llo", "hello")]
    [InlineData("a‌b‍c⁠d", "abcd")]
    [InlineData("﻿bom prefix", "bom prefix")]
    [InlineData("x⁡y⁢z⁣w⁤v", "xyzwv")]
    // Bidi overrides/embeddings/isolates
    [InlineData("ab‮cd", "abcd")]
    [InlineData("safe ‪hidden‬ text", "safe hidden text")]
    [InlineData("x⁦y⁧z⁨w⁩v", "xyzwv")]
    public void Dado_TextoComCaracteresInvisiveis_Quando_Sanitize_Entao_RemoveTodos(string input, string expected)
        => UntrustedContentSanitizer.Sanitize(input).ShouldBe(expected);

    [Fact]
    public void Dado_HomoglyphCirilicoAdjacenteAAscii_Quando_Sanitize_Entao_SinalizaComReplacementChar()
    {
        // 'pаypal' — o 'а' é cirílico (U+0430), phishing clássico.
        UntrustedContentSanitizer.Sanitize("visit pаypal.com now")
            .ShouldBe("visit p�ypal.com now");
    }

    [Fact]
    public void Dado_TextoCirilicoPuro_Quando_Sanitize_Entao_Preserva()
    {
        // Run todo não-ASCII (sem adjacência a ASCII) — provável texto legítimo.
        UntrustedContentSanitizer.Sanitize("титул привет")
            .ShouldBe("титул привет");
    }

    [Fact]
    public void Dado_AcentosLatinos_Quando_Sanitize_Entao_Preserva()
    {
        // PT-BR legítimo não é confusable — não pode ser manglado.
        const string pt = "Você criou a ação correta — endereço válido.";
        UntrustedContentSanitizer.Sanitize(pt).ShouldBe(pt);
    }

    [Fact]
    public void Dado_TextoJaSanitizado_Quando_Sanitize_Entao_Idempotente()
    {
        const string dirty = "a​b‮ pаypal ⁠";
        var once = UntrustedContentSanitizer.Sanitize(dirty);
        UntrustedContentSanitizer.Sanitize(once).ShouldBe(once);
    }

    [Fact]
    public void Dado_NullOuVazio_Quando_Sanitize_Entao_RetornaComoEsta()
    {
        UntrustedContentSanitizer.Sanitize("").ShouldBe("");
        UntrustedContentSanitizer.Sanitize(null!).ShouldBeNull();
    }
}
