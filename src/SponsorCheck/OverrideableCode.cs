public sealed record OverrideableCode(string Code, string Stem)
{
    public string SeverityMetadataName => $"{Stem}SeverityOverride";
    public string MessageMetadataName => $"{Stem}MessageOverride";
}

public static class OverrideableCodes
{
    // Single source of truth for the codes the OSS author can tune at pack time. The bundler,
    // verifier, targets file, and docs all derive their metadata names from this list. A new
    // entry needs accompanying bundler properties + targets-file plumbing.
    public static readonly OverrideableCode[] All =
    [
        new("SC001", "NoLicenseSpecified"),
        new("SC003", "LicenseIgnored"),
        new("SC004", "InvalidAccount"),
        new("SC005", "LicenseExpired"),
    ];

    public static HashSet<string> Codes { get; } =
        new(All.Select(_ => _.Code), StringComparer.Ordinal);
}
