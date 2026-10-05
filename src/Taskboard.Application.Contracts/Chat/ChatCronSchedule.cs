namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Minimal 5-field cron evaluator (minute hour day-of-month month day-of-week)
/// for cron ChatSchedule rows (SPEC-20261005-chat-jobs-schedule-search
/// RF-004). Supports <c>*</c>, <c>*/n</c>, single values, ranges
/// (<c>a-b</c>), stepped ranges (<c>a-b/n</c>) and comma lists; day-of-week
/// accepts 0–7 (0 and 7 = Sunday) and <c>?</c> as <c>*</c>. When both
/// day-of-month and day-of-week are restricted they union (standard cron).
/// No external cron dependency — the repo carries none and a NuGet add is
/// unjustifiable for five fields.
/// </summary>
public sealed class ChatCronSchedule
{
    private const int MaxYearsAhead = 4;

    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _daysOfMonth = new bool[32]; // 1..31
    private readonly bool[] _months = new bool[13]; // 1..12
    private readonly bool[] _daysOfWeek = new bool[7]; // 0=Sun..6=Sat
    private readonly bool _domRestricted;
    private readonly bool _dowRestricted;

    private ChatCronSchedule(bool domRestricted, bool dowRestricted)
    {
        _domRestricted = domRestricted;
        _dowRestricted = dowRestricted;
    }

    /// <summary>Parses the 5-field expression — throws <see cref="FormatException"/> on bad input.</summary>
    public static ChatCronSchedule Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("Cron expression cannot be empty.");
        }

        var fields = expression.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException($"Cron expression must have 5 fields, got {fields.Length}.");
        }

        var schedule = new ChatCronSchedule(
            domRestricted: !IsWildcard(fields[2]),
            dowRestricted: !IsWildcard(fields[4]));

        FillField(fields[0], 0, 59, schedule._minutes);
        FillField(fields[1], 0, 23, schedule._hours);
        FillField(fields[2], 1, 31, schedule._daysOfMonth);
        FillField(fields[3], 1, 12, schedule._months);
        FillDayOfWeekField(fields[4], schedule._daysOfWeek);

        return schedule;
    }

    /// <summary>First fire instant strictly after <paramref name="fromUtc"/>.</summary>
    public DateTime NextAfter(DateTime fromUtc, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var candidate = new DateTime(
            fromUtc.Ticks - (fromUtc.Ticks % TimeSpan.TicksPerMinute) + TimeSpan.TicksPerMinute,
            DateTimeKind.Utc);
        var limit = fromUtc.AddYears(MaxYearsAhead);

        while (candidate < limit)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(candidate, tz);

            if (!_months[local.Month])
            {
                candidate = NextBoundaryUtc(candidate, tz, boundary: new DateTime(local.Year, local.Month, 1).AddMonths(1));
                continue;
            }

            if (!DayMatches(local))
            {
                candidate = NextBoundaryUtc(candidate, tz, boundary: local.Date.AddDays(1));
                continue;
            }

            if (!_hours[local.Hour])
            {
                candidate = NextBoundaryUtc(candidate, tz, boundary: local.Date.AddHours(local.Hour + 1));
                continue;
            }

            if (!_minutes[local.Minute])
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return candidate;
        }

        throw new FormatException("Cron expression produces no fire time within 4 years.");
    }

    private bool DayMatches(DateTime local)
    {
        var domMatch = _daysOfMonth[local.Day];
        var dowMatch = _daysOfWeek[(int)local.DayOfWeek];
        return (_domRestricted, _dowRestricted) switch
        {
            (true, true) => domMatch || dowMatch,
            (true, false) => domMatch,
            (false, true) => dowMatch,
            _ => true,
        };
    }

    /// <summary>
    /// Jumps to the next local <paramref name="boundary"/> (month/day/hour
    /// rollover) as the corresponding UTC instant — DST-safe by construction.
    /// </summary>
    private static DateTime NextBoundaryUtc(DateTime currentUtc, TimeZoneInfo tz, DateTime boundary)
    {
        var local = new DateTime(boundary.Ticks, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return utc > currentUtc ? utc : currentUtc.AddMinutes(1);
    }

    private static bool IsWildcard(string field) =>
        field is "*" or "?" || field.StartsWith("*/", StringComparison.Ordinal);

    private static void FillDayOfWeekField(string field, bool[] target)
    {
        if (field.Contains('?'))
        {
            field = field.Replace('?', '*');
        }

        var temp = new bool[8];
        FillField(field, 0, 7, temp);
        for (var i = 0; i < 7; i++)
        {
            target[i] = temp[i] || (i == 0 && temp[7]);
        }
    }

    private static void FillField(string field, int min, int max, bool[] target)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rangeText = part;
            var step = 1;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], out step) || step < 1)
                {
                    throw new FormatException($"Invalid cron step in '{part}'.");
                }

                rangeText = part[..slash];
            }

            int lo;
            int hi;
            if (rangeText is "*" or "?")
            {
                lo = min;
                hi = max;
            }
            else
            {
                var dash = rangeText.IndexOf('-');
                if (dash < 0)
                {
                    lo = hi = ParseNumber(rangeText, min, max);
                }
                else
                {
                    lo = ParseNumber(rangeText[..dash], min, max);
                    hi = ParseNumber(rangeText[(dash + 1)..], min, max);
                    if (hi < lo)
                    {
                        throw new FormatException($"Invalid cron range '{rangeText}'.");
                    }
                }
            }

            for (var v = lo; v <= hi; v += step)
            {
                target[v] = true;
            }
        }
    }

    private static int ParseNumber(string text, int min, int max)
    {
        if (!int.TryParse(text, out var value) || value < min || value > max)
        {
            throw new FormatException($"Cron value '{text}' out of range {min}-{max}.");
        }

        return value;
    }
}
