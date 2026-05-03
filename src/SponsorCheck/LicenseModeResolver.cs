public abstract record LicenseDecision(string PackageId)
{
    public sealed record MissingConfig(string PackageId) : LicenseDecision(PackageId);
    public sealed record ConflictingModes(string PackageId, IReadOnlyList<string> Modes) : LicenseDecision(PackageId);
    public sealed record Ignored(string PackageId) : LicenseDecision(PackageId);
    public sealed record Sponsor(string PackageId, IReadOnlyDictionary<string, string> AccountByPlatform, string? SponsorshipStartRaw) : LicenseDecision(PackageId);
    public sealed record Licensed(string PackageId, string LicensedUntilRaw) : LicenseDecision(PackageId);
}

public static class LicenseModeResolver
{
    public static LicenseDecision Resolve(
        string? ignored,
        string? licensedUntil,
        IReadOnlyDictionary<string, string?> sponsorAccountsByPlatform,
        string? sponsorshipStart,
        string packageId)
    {
        var modes = new List<string>();
        var isIgnored = string.Equals(ignored, "true", StringComparison.OrdinalIgnoreCase);
        if (isIgnored)
        {
            modes.Add("SponsorshipLicenseIgnored");
        }

        var nonEmptySponsors = sponsorAccountsByPlatform
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
        if (nonEmptySponsors.Count > 0)
        {
            modes.Add("Sponsor");
        }

        var hasLicense = !string.IsNullOrWhiteSpace(licensedUntil);
        if (hasLicense)
        {
            modes.Add("SponsorshipLicensedUntil");
        }

        if (modes.Count == 0)
        {
            return new LicenseDecision.MissingConfig(packageId);
        }

        if (modes.Count > 1)
        {
            return new LicenseDecision.ConflictingModes(packageId, modes);
        }

        if (isIgnored)
        {
            return new LicenseDecision.Ignored(packageId);
        }

        if (nonEmptySponsors.Count > 0)
        {
            var startNormalized = string.IsNullOrWhiteSpace(sponsorshipStart) ? null : sponsorshipStart!.Trim();
            return new LicenseDecision.Sponsor(packageId, nonEmptySponsors, startNormalized);
        }

        return new LicenseDecision.Licensed(packageId, licensedUntil!.Trim());
    }
}
