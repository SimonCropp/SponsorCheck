namespace EnforceOssSponsorship.Tasks;

using System.Globalization;

public sealed class VerifySponsorshipTask : Microsoft.Build.Utilities.Task
{
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string SponsorHashListPath { get; set; } = "";

    public string IgnoredFromRef { get; set; } = "";
    public string IgnoredFromVer { get; set; } = "";
    public string LicensedUntilFromRef { get; set; } = "";
    public string LicensedUntilFromVer { get; set; } = "";
    public string GitHubFromRef { get; set; } = "";
    public string GitHubFromVer { get; set; } = "";
    public string OpenCollectiveFromRef { get; set; } = "";
    public string OpenCollectiveFromVer { get; set; } = "";
    public string PolarFromRef { get; set; } = "";
    public string PolarFromVer { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var ignored = PackageMetadataMerger.Merge("SponsorshipIgnored", IgnoredFromRef, IgnoredFromVer);
            var licensedUntil = PackageMetadataMerger.Merge("SponsorshipLicensedUntil", LicensedUntilFromRef, LicensedUntilFromVer);
            var sponsors = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GitHubSponsors"] = PackageMetadataMerger.Merge("GitHubSponsorAccount", GitHubFromRef, GitHubFromVer),
                ["OpenCollective"] = PackageMetadataMerger.Merge("OpenCollectiveSponsorAccount", OpenCollectiveFromRef, OpenCollectiveFromVer),
                ["Polar"] = PackageMetadataMerger.Merge("PolarSponsorAccount", PolarFromRef, PolarFromVer)
            };

            var decision = LicenseModeResolver.Resolve(ignored, licensedUntil, sponsors, ThePackageId);
            return DecisionApplier.Apply(decision, SponsorHashListPath, Log, DateTime.UtcNow);
        }
        catch (MaintenanceFeeException ex)
        {
            Log.LogError("EOSS", "EOSS006", "", "", 0, 0, 0, 0, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }
}

public static class DecisionApplier
{
    public static bool Apply(LicenseDecision decision, string sponsorHashListPath, TaskLoggingHelper log, DateTime utcNow)
    {
        switch (decision)
        {
            case LicenseDecision.MissingConfig m:
                log.LogError(
                    "EOSS",
                    "EOSS001",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{m.PackageId}' is built with EnforceOssSponsorship and requires one license-mode metadata: SponsorshipIgnored=\"true\", a <Platform>SponsorAccount, or SponsorshipLicensedUntil=\"yyyy-MM\". See https://opensourcemaintenancefee.org/.");
                return false;

            case LicenseDecision.ConflictingModes c:
                log.LogError(
                    "EOSS",
                    "EOSS002",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{c.PackageId}': mutually exclusive license modes set ({string.Join(", ", c.Modes)}). Pick one.");
                return false;

            case LicenseDecision.Ignored i:
                log.LogWarning(
                    "EOSS",
                    "EOSS003",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{i.PackageId}': SponsorshipIgnored=\"true\". Build is allowed but you are not honoring the OSS Maintenance Fee. See https://opensourcemaintenancefee.org/.");
                return true;

            case LicenseDecision.Sponsor s:
                return ApplySponsor(s, sponsorHashListPath, log);

            case LicenseDecision.Licensed l:
                return ApplyLicensed(l, log, utcNow);

            default:
                throw new InvalidOperationException($"Unknown decision: {decision.GetType().Name}");
        }
    }

    static bool ApplySponsor(LicenseDecision.Sponsor s, string sponsorHashListPath, TaskLoggingHelper log)
    {
        if (!File.Exists(sponsorHashListPath))
        {
            log.LogError(
                "EOSS",
                "EOSS010",
                "",
                "",
                0,
                0,
                0,
                0,
                $"Package '{s.PackageId}': bundled sponsor hash file not found at '{sponsorHashListPath}'.");
            return false;
        }

        var hashes = new HashSet<string>(File.ReadAllLines(sponsorHashListPath), StringComparer.Ordinal);
        var attempts = new List<string>();
        foreach (var pair in s.AccountByPlatform)
        {
            var hash = SponsorHasher.Hash(pair.Key, pair.Value);
            if (hashes.Contains(hash))
            {
                return true;
            }

            attempts.Add($"{pair.Key}={pair.Value}");
        }

        log.LogError(
            "EOSS",
            "EOSS004",
            "",
            "",
            0,
            0,
            0,
            0,
            $"Package '{s.PackageId}': no supplied sponsor account matches the bundled list (tried: {string.Join(", ", attempts)}). Visit https://github.com/sponsors and pick a tier, then set the matching <Platform>SponsorAccount metadata.");
        return false;
    }

    static bool ApplyLicensed(LicenseDecision.Licensed l, TaskLoggingHelper log, DateTime utcNow)
    {
        if (!TryParseYearMonth(l.LicensedUntilRaw, out var year, out var month))
        {
            log.LogError(
                "EOSS",
                "EOSS007",
                "",
                "",
                0,
                0,
                0,
                0,
                $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' is not in 'yyyy-MM' format.");
            return false;
        }

        var lastDay = DateTime.DaysInMonth(year, month);
        var endOfMonth = new DateTime(year, month, lastDay, 23, 59, 59, DateTimeKind.Utc);
        if (utcNow > endOfMonth)
        {
            log.LogError(
                "EOSS",
                "EOSS005",
                "",
                "",
                0,
                0,
                0,
                0,
                $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' has expired (end of month {endOfMonth:yyyy-MM-dd} UTC).");
            return false;
        }

        return true;
    }

    static bool TryParseYearMonth(string value, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return false;
        }

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }
}
