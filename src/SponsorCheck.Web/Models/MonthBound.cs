namespace SponsorCheck.Web.Models;

/// <summary>
/// The month-window arithmetic the verifier applies to every <c>...Until</c> value, restated for the
/// wizard. Mirrors <c>DecisionApplier</c> in the task assembly, which the wizard cannot reference:
/// a claim is valid through the end of the month it names (so the current month still passes), and a
/// capped claim may name at most <c>maxTermMonths</c> past the build month. Both comparisons are on
/// calendar fields rather than <see cref="DateTime.AddMonths"/> for the same reason the verifier does
/// it that way — a claim at the calendar extreme must not overflow.
///
/// Pure functions over an explicit <c>utcNow</c>, so <see cref="ConsumerModel"/> stays clock-free and
/// the callers stay testable. <c>RepoContractTests</c> pins the arithmetic against the verifier's.
/// </summary>
public static class MonthBound
{
    /// <summary>The one-year ceiling the verifier hard-codes for SponsorshipLicensedUntil
    /// (SC035/SC036/SC037), expressed in the same months unit as every other cap.</summary>
    public const int LicensedUntilMaxTermMonths = 12;

    public static bool TryParse(string? value, out int year, out int month)
    {
        year = 0;
        month = 0;
        var trimmed = value?.Trim() ?? "";
        if (!DateTime.TryParseExact(trimmed, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }

    /// <summary>True once the build month is past the named month. A value that does not parse is not
    /// expired — the format error is the caller's separate, already-reported problem.</summary>
    public static bool IsExpired(string? value, DateTime utcNow) =>
        TryParse(value, out var year, out var month) &&
        (utcNow.Year > year || (utcNow.Year == year && utcNow.Month > month));

    /// <summary>True when the named month is past the ceiling a claim capped at
    /// <paramref name="maxTermMonths"/> may reach.</summary>
    public static bool IsBeyondCeiling(string? value, DateTime utcNow, int maxTermMonths)
    {
        if (!TryParse(value, out var year, out var month))
        {
            return false;
        }

        var (ceilingYear, ceilingMonth) = AddMonths(utcNow, maxTermMonths);
        return year > ceilingYear || (year == ceilingYear && month > ceilingMonth);
    }

    /// <summary>The last month such a claim may name, rendered for display.</summary>
    public static string Ceiling(DateTime utcNow, int maxTermMonths)
    {
        var (year, month) = AddMonths(utcNow, maxTermMonths);
        return $"{year:0000}-{month:00}";
    }

    static (int Year, int Month) AddMonths(DateTime utcNow, int months)
    {
        var total = utcNow.Year * 12 + (utcNow.Month - 1) + months;
        return (total / 12, total % 12 + 1);
    }
}
