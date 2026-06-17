public abstract record LicenseDecision(string PackageId)
{
    public sealed record MissingConfig(string PackageId) : LicenseDecision(PackageId);
    public sealed record ConflictingModes(string PackageId, IReadOnlyList<string> Modes) : LicenseDecision(PackageId);
    public sealed record Ignored(string PackageId) : LicenseDecision(PackageId);
    public sealed record Exempt(string PackageId, string ExemptionName) : LicenseDecision(PackageId);
    public sealed record Sponsor(string PackageId, IReadOnlyDictionary<string, string> AccountByPlatform, string? SponsorshipStartRaw) : LicenseDecision(PackageId);
    public sealed record Licensed(string PackageId, string LicensedUntilRaw) : LicenseDecision(PackageId);
}