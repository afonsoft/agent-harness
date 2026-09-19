using Shouldly;
using Taskboard.Integrations.Harness.Verification;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260919-harness-verification-loop T2 — parsers de build/test/cobertura.</summary>
public class VerificationParsersTests
{
    [Fact]
    public void Dado_BuildComErros_Quando_ParseErrors_Entao_ExtraiArquivoLinhaCodigo()
    {
        const string output = """
            Restored /repo/src/A.csproj (in 100 ms).
            src/Taskboard.Server/Program.cs(42,10): error CS0246: The type or namespace name 'X' could not be found [/repo/src/Taskboard.Server/Taskboard.Server.csproj]
            src/Foo/Bar.cs(7,3): warning CS0168: The variable 'y' is declared but never used [/repo/src/Foo/Foo.csproj]
            Build FAILED.
            """;

        var errors = CompilerErrorParser.Parse(output);

        errors.Count.ShouldBe(2);
        errors[0].File.ShouldBe("src/Taskboard.Server/Program.cs");
        errors[0].Line.ShouldBe(42);
        errors[0].Column.ShouldBe(10);
        errors[0].ErrorCode.ShouldBe("CS0246");
        errors[0].Message.ShouldContain("type or namespace name 'X'");
        errors[1].ErrorCode.ShouldBe("CS0168"); // warning vira erro com TWAE
    }

    [Fact]
    public void Dado_BuildLimpo_Quando_ParseErrors_Entao_ListaVazia()
    {
        var errors = CompilerErrorParser.Parse("Build succeeded.\n    0 Warning(s)\n    0 Error(s)");

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_TrxComFalhas_Quando_ParseFailures_Entao_ExtraiNomeEMensagem()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Taskboard.Tests.Unit.HarnessTests.Dado_X_Entao_Y" outcome="Failed">
                  <Output><ErrorInfo>
                    <Message>Shouldly.ShouldAssertException: Expected true but was false</Message>
                    <StackTrace>   at HarnessTests.Dado_X_Entao_Y() in Tests.cs:line 12</StackTrace>
                  </ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testName="Taskboard.Tests.Unit.OkTest" outcome="Passed" />
              </Results>
              <ResultSummary outcome="Failed"><Counters total="2" passed="1" failed="1" /></ResultSummary>
            </TestRun>
            """;

        var summary = TestFailureParser.Parse(trx);

        summary.Total.ShouldBe(2);
        summary.Passed.ShouldBe(1);
        summary.Failed.ShouldBe(1);
        summary.Failures.Count.ShouldBe(1);
        summary.Failures[0].Name.ShouldBe("Taskboard.Tests.Unit.HarnessTests.Dado_X_Entao_Y");
        summary.Failures[0].Message.ShouldContain("Expected true but was false");
        summary.Failures[0].StackTrace.ShouldNotBeNull().ShouldContain("Tests.cs");
    }

    [Fact]
    public void Dado_TrxVerde_Quando_ParseFailures_Entao_SemFalhas()
    {
        const string trx = """
            <?xml version="1.0"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results><UnitTestResult testName="T1" outcome="Passed" /></Results>
              <ResultSummary outcome="Completed"><Counters total="1" passed="1" failed="0" /></ResultSummary>
            </TestRun>
            """;

        var summary = TestFailureParser.Parse(trx);

        summary.Failed.ShouldBe(0);
        summary.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_CoberturaXml_Quando_Parse_Entao_RetornaPercentual()
    {
        const string cobertura = """
            <?xml version="1.0"?>
            <coverage line-rate="0.6626" branch-rate="0.5" version="1.9">
              <packages />
            </coverage>
            """;

        CoverageCalculator.ParseLineRate(cobertura).ShouldBe(66.26, tolerance: 0.01);
    }

    [Fact]
    public void Dado_CoberturaInvalida_Quando_Parse_Entao_Zero()
        => CoverageCalculator.ParseLineRate("<not-xml").ShouldBe(0.0);
}
