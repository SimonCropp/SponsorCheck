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
            .Where(_ => !string.IsNullOrWhiteSpace(_.Value))
            .ToDictionary(_ => _.Key, _ => _.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
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
