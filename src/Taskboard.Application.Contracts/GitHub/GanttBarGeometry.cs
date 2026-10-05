namespace Taskboard.GitHub;

/// <summary>
/// Percent geometry for the Gantt bars (SPEC-20261010-gantt-bar-clamp):
/// left = created date clamped to the window, width ≥ 1% and left + width ≤
/// 100% so a bar never renders outside the track — and the clamp never
/// receives min &gt; max (the <c>Argument_MinMaxValue</c> crash thrown when an
/// issue is created on the last day of the window → left = 100 → max = 0).
/// Pure functions: the page is a Blazor component, so the math lives in this
/// shared contract assembly to stay unit-testable.
/// </summary>
public static class GanttBarGeometry
{
    /// <summary>Bar offset in % of the window width ([0,100]).</summary>
    public static double LeftPercent(DateTime rangeStart, DateTime rangeEnd, DateTime createdUtcDate)
    {
        var total = TotalDays(rangeStart, rangeEnd);
        if (total <= 0)
        {
            return 0;
        }

        var start = createdUtcDate < rangeStart ? rangeStart : createdUtcDate;
        return Math.Clamp((start - rangeStart).TotalDays / total * 100, 0, 100);
    }

    /// <summary>
    /// Bar width in %, clamped to [1, 100 − <paramref name="leftPercent"/>] —
    /// with the max floored at 1 so the clamp bounds can never invert. Open
    /// issues (<paramref name="closedUtcDate"/> null) extend to
    /// <paramref name="rangeEnd"/>.
    /// </summary>
    public static double WidthPercent(
        DateTime rangeStart, DateTime rangeEnd, DateTime createdUtcDate,
        DateTime? closedUtcDate, double leftPercent)
    {
        var total = TotalDays(rangeStart, rangeEnd);
        if (total <= 0)
        {
            return 100;
        }

        var start = createdUtcDate < rangeStart ? rangeStart : createdUtcDate;
        var end = closedUtcDate ?? rangeEnd;
        if (end > rangeEnd)
        {
            end = rangeEnd;
        }

        var days = Math.Max((end - start).TotalDays, 1);
        return Math.Clamp(days / total * 100, 1, Math.Max(100 - leftPercent, 1));
    }

    /// <summary>
    /// (left, width) pair guaranteed to satisfy left + width ≤ 100: when the
    /// floored minimum width (an issue created on the last window day) would
    /// push the bar past the right edge, the bar is pulled left into the
    /// track instead.
    /// </summary>
    public static (double Left, double Width) BarPercent(
        DateTime rangeStart, DateTime rangeEnd, DateTime createdUtcDate, DateTime? closedUtcDate)
    {
        var left = LeftPercent(rangeStart, rangeEnd, createdUtcDate);
        var width = WidthPercent(rangeStart, rangeEnd, createdUtcDate, closedUtcDate, left);
        if (left + width > 100)
        {
            left = Math.Max(0, 100 - width);
        }

        return (left, width);
    }

    private static double TotalDays(DateTime rangeStart, DateTime rangeEnd) =>
        (rangeEnd - rangeStart).TotalDays;
}
