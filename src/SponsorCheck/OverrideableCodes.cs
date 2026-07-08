public static class OverrideableCodes
{
    // Single source of truth for the codes the OSS author can tune at pack time. The bundler,
    // verifier, targets file, and docs all derive their metadata names from this list. A new
    // entry needs accompanying bundler properties + targets-file plumbing.
    public static readonly OverrideableCode[] All =
    [
        new("SC001", "SC002", "SC021", "NoLicenseSpecified"),
        new("SC005", "SC006", "SC023", "LicenseIgnored"),
        new("SC007", "SC008", "SC024", "InvalidAccount"),
        new("SC009", "SC010", "SC025", "LicenseExpired"),
    ];

    public static HashSet<string> Codes { get; } =
        [with(All.SelectMany(_ => new[] {_.Code, _.CpmCode, _.OwnerCode}), StringComparer.Ordinal)];
}