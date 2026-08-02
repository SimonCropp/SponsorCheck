namespace SponsorCheck.Web.Models;

public sealed class ExemptionEntry
{
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>Optional cap, as typed. Held as a string because it is bound to a text input and an
    /// unparseable value has to survive round-tripping so the wizard can report it (SC106) rather
    /// than silently discarding it.</summary>
    public string MaxTermMonths { get; set; } = "";

    public bool HasMaxTermMonths => MaxTermMonths.Trim().Length > 0;

    /// <summary>Matches the bundler's parse: a positive whole number, no sign, no decimal point.</summary>
    public bool IsMaxTermMonthsValid =>
        !HasMaxTermMonths ||
        (int.TryParse(MaxTermMonths.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var months) && months > 0);

    public int? ParsedMaxTermMonths =>
        IsMaxTermMonthsValid && HasMaxTermMonths
            ? int.Parse(MaxTermMonths.Trim(), NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    public bool IsComplete => Name.Trim().Length > 0 && Message.Trim().Length > 0 && IsMaxTermMonthsValid;
    public bool IsBlank => Name.Trim().Length == 0 && Message.Trim().Length == 0 && !HasMaxTermMonths;
}
