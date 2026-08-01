namespace SponsorCheck.Web.Models;

/// <summary>
/// One overrideable verifier condition. The author tunes severity and/or message text via the
/// <c>{Stem}SeverityOverride</c> / <c>{Stem}MessageOverride</c> metadata; a single value covers the
/// non-CPM, CPM, and owner-mode sibling codes. Mirrors src/SponsorCheck/OverrideableCodes.cs, which
/// the anti-rot tests compare against.
/// </summary>
public sealed record OverrideInfo(
    OverrideKind Kind,
    string DisplayName,
    string Stem,
    string DefaultSeverity,
    string Codes)
{
    public string SeverityMetadata => $"{Stem}SeverityOverride";
    public string MessageMetadata => $"{Stem}MessageOverride";

    public static readonly OverrideInfo NoLicenseSpecified = new(
        OverrideKind.NoLicenseSpecified, "No license specified", "NoLicenseSpecified", "error", "SC001/SC002/SC021");

    public static readonly OverrideInfo LicenseIgnored = new(
        OverrideKind.LicenseIgnored, "License ignored", "LicenseIgnored", "warning", "SC005/SC006/SC023");

    public static readonly OverrideInfo InvalidAccount = new(
        OverrideKind.InvalidAccount, "Sponsor account not in list", "InvalidAccount", "error", "SC007/SC008/SC024");

    public static readonly OverrideInfo LicenseExpired = new(
        OverrideKind.LicenseExpired, "License expired", "LicenseExpired", "error", "SC009/SC010/SC025");

    public static readonly IReadOnlyList<OverrideInfo> All =
        [NoLicenseSpecified, LicenseIgnored, InvalidAccount, LicenseExpired];
}
