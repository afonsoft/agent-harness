using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage P1 testes: `claude /usage` fixtures —
/// 5h/weekly windows, reset formats ("resets in 3h", "resets at 5pm"),
/// multi-block kind collision keeps highest pct, unparseable → empty.
/// </summary>
public class ClaudeUsageParserTests
{
    private static readonly DateTime Now =
        new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Dado_OutputTipico_Quando_Parse_Entao_WindowsPorKind()
    {
        const string output = """
            Current session
            ████████████░░░░░ 62% used
            Resets in 3h 12m

            Current week (all models)
            ███░░░ 21% used
            Resets at 5pm

            Current week (Sonnet only)
            5% used
            Resets at 5pm
            """;

        var windows = ClaudeUsageParser.Parse(output, Now);

        windows.Count.ShouldBe(2);
        var five = windows.Single(w => w.WindowKind == ProviderWindowKinds.FiveHour);
        five.UsedPct.ShouldBe(62);
        five.ResetsAtUtc.ShouldBe(Now.AddHours(3).AddMinutes(12));
        five.Source.ShouldBe(ProviderUsageSources.Cli);
        five.Confidence.ShouldBe(ProviderUsageConfidences.Measured);

        // two weekly blocks → highest pct wins.
        windows.Single(w => w.WindowKind == ProviderWindowKinds.Weekly)
            .UsedPct.ShouldBe(21);
    }

    [Fact]
    public void Dado_ResetsAt5pm_Quando_Passou_Quando_Parse_Entao_Amanha()
    {
        const string output = "Current week\n10% used\nResets at 5pm";

        var resets = ClaudeUsageParser.Parse(output, Now).Single().ResetsAtUtc;

        // 10:00 UTC — 17:00 today is still ahead.
        resets.ShouldBe(new DateTime(2026, 10, 21, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_ResetsAt8am_Quando_JaPassou_Entao_Amanha()
    {
        const string output = "Current week\n10% used\nResets at 8am";

        var resets = ClaudeUsageParser.Parse(output, Now).Single().ResetsAtUtc;

        resets.ShouldBe(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_ResetsAtComMinutos_Quando_Parse_Entao_HoraMinuto()
    {
        const string output = "Current day\n40% used\nResets at 11:30pm";

        var windows = ClaudeUsageParser.Parse(output, Now);

        var daily = windows.Single(w => w.WindowKind == ProviderWindowKinds.Daily);
        daily.ResetsAtUtc.ShouldBe(new DateTime(2026, 10, 21, 23, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_ResetsInMinutos_Quando_Parse_Entao_Delta()
    {
        const string output = "Current session\n90% used\nResets in 45m";

        var resets = ClaudeUsageParser.Parse(output, Now).Single().ResetsAtUtc;

        resets.ShouldBe(Now.AddMinutes(45));
    }

    [Fact]
    public void Dado_SemJanelas_Quando_Parse_Entao_Vazio()
    {
        ClaudeUsageParser.Parse("usage: unknown command\n", Now).ShouldBeEmpty();
        ClaudeUsageParser.Parse("", Now).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_BlocoSemReset_Quando_Parse_Entao_ResetsNull()
    {
        const string output = "Current session\n62% used";

        ClaudeUsageParser.Parse(output, Now).Single().ResetsAtUtc.ShouldBeNull();
    }
}
