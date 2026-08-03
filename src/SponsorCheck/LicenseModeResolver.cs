public static class LicenseModeResolver
{
    public static LicenseDecision Resolve(
        string? ignored,
        string? licensedUntil,
        string? exemption,
        string? exemptionUntil,
        IReadOnlyDictionary<string, string?> sponsorAccountsByPlatform,
        string? sponsorshipStart,
        string packageId)
    {
        // Order matters: messages list modes in this order so SponsorshipLicenseIgnored (the
        // breach-of-license escape hatch) appears last, after the legitimate options. The
        // exemption falls between a time-bounded license and the breach hatch — it's a
        // legitimate publisher-sanctioned carve-out, not a breach.
        var modes = new List<string>();
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

        // SponsorshipExemptionUntil deliberately does not count as a mode of its own — it bounds an
        // exemption claim the same way SponsorshipStart qualifies a sponsor claim. Set on its own it
        // selects nothing, so the consumer still gets the MissingConfig diagnostic.
        var hasExemption = !string.IsNullOrWhiteSpace(exemption);
        if (hasExemption)
        {
            modes.Add("SponsorshipExemption");
        }

        var isIgnored = string.Equals(ignored, "true", StringComparison.OrdinalIgnoreCase);
        if (isIgnored)
        {
            modes.Add("SponsorshipLicenseIgnored");
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

        if (hasExemption)
        {
            var untilNormalized = string.IsNullOrWhiteSpace(exemptionUntil) ? null : exemptionUntil!.Trim();
            return new LicenseDecision.Exempt(packageId, exemption!.Trim(), untilNormalized);
        }

        if (nonEmptySponsors.Count > 0)
        {
            var startNormalized = string.IsNullOrWhiteSpace(sponsorshipStart) ? null : sponsorshipStart!.Trim();
            return new LicenseDecision.Sponsor(packageId, nonEmptySponsors, startNormalized);
        }

        return new LicenseDecision.Licensed(packageId, licensedUntil!.Trim());
    }
}
