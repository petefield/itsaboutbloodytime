using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricalTimeline.Api.Models;

public readonly partial record struct HistoricalDate(int Year, int Month, int Day, long Ordinal)
{
    private static readonly Regex DatePattern = DateRegex();

    public string Text => Year < 0
        ? $"-{Math.Abs(Year).ToString("D4", CultureInfo.InvariantCulture)}-{Month:D2}-{Day:D2}"
        : $"{Year:D4}-{Month:D2}-{Day:D2}";

    public static bool TryParse(string? value, out HistoricalDate date)
    {
        date = default;
        var match = DatePattern.Match(value?.Trim() ?? string.Empty);
        if (!match.Success
            || !int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out var year)
            || (year >= 0 && !match.Groups["month"].Success)
            || !int.TryParse(match.Groups["month"].Success ? match.Groups["month"].Value : "1", CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(match.Groups["day"].Success ? match.Groups["day"].Value : "1", CultureInfo.InvariantCulture, out var day)
            || month is < 1 or > 12
            || day < 1
            || day > DaysInMonth(year, month))
        {
            return false;
        }

        date = new HistoricalDate(year, month, day, DaysFromCivil(year, month, day));
        return true;
    }

    private static int DaysInMonth(int year, int month) => month switch
    {
        2 when IsLeapYear(year) => 29,
        2 => 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31
    };

    private static bool IsLeapYear(int year) =>
        year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    // Returns a proleptic Gregorian day ordinal relative to 1970-01-01.
    private static long DaysFromCivil(int year, int month, int day)
    {
        var adjustedYear = year - (month <= 2 ? 1 : 0);
        var era = (adjustedYear >= 0 ? adjustedYear : adjustedYear - 399) / 400;
        var yearOfEra = adjustedYear - (era * 400);
        var dayOfYear = ((153 * (month + (month > 2 ? -3 : 9))) + 2) / 5 + day - 1;
        return (era * 146097L) + (yearOfEra * 365L) + (yearOfEra / 4) - (yearOfEra / 100) + dayOfYear - 719468;
    }

    [GeneratedRegex(@"^(?<year>-\d{4,6}|\d{4})-(?<month>\d{2})-(?<day>\d{2})(?:T00:00:00(?:Z)?)?$")]
    private static partial Regex DateRegex();
}
