public static class DecisionApplier
{
    public static bool Apply(LicenseDecision decision, string sponsorHashListPath, string packDatePath, TaskLoggingHelper log, DateTime utcNow)
    {
        switch (decision)
        {
            case LicenseDecision.MissingConfig m:
                log.LogError(
                    "SponsorCheck",
                    "SC001",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{m.PackageId}' is built with SponsorCheck and requires one license-mode metadata: SponsorshipLicenseIgnored=\"true\", a <Platform>SponsorAccount, or SponsorshipLicensedUntil=\"yyyy-MM\". See https://opensourcemaintenancefee.org/.");
                return false;

            case LicenseDecision.ConflictingModes c:
                log.LogError(
                    "SponsorCheck",
                    "SC002",
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
                    "SponsorCheck",
                    "SC003",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{i.PackageId}': SponsorshipLicenseIgnored=\"true\". Build is allowed but you are not honoring the OSS Maintenance Fee. See https://opensourcemaintenancefee.org/.");
                return true;

            case LicenseDecision.Sponsor s:
                return ApplySponsor(s, sponsorHashListPath, packDatePath, log, utcNow);

            case LicenseDecision.Licensed l:
                return ApplyLicensed(l, log, utcNow);

            default:
                throw new InvalidOperationException($"Unknown decision: {decision.GetType().Name}");
        }
    }

    static bool ApplySponsor(LicenseDecision.Sponsor s, string sponsorHashListPath, string packDatePath, TaskLoggingHelper log, DateTime utcNow)
    {
        // If consumer declared SponsorshipStart, see if they signed up after the package was packed.
        // If so, the bundled hash couldn't possibly know about them — trust the declaration.
        if (!string.IsNullOrWhiteSpace(s.SponsorshipStartRaw))
        {
            if (!TryParseDate(s.SponsorshipStartRaw!, out var startDate))
            {
                log.LogError(
                    "SponsorCheck",
                    "SC013",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{s.PackageId}': SponsorshipStart='{s.SponsorshipStartRaw}' is not in 'yyyy-MM-dd' format.");
                return false;
            }

            if (startDate > utcNow.Date)
            {
                log.LogError(
                    "SponsorCheck",
                    "SC014",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    $"Package '{s.PackageId}': SponsorshipStart='{s.SponsorshipStartRaw}' is in the future.");
                return false;
            }

            var packDate = TryReadPackDate(packDatePath);
            if (packDate is { } pd && startDate > pd)
            {
                var attempts = string.Join(", ", s.AccountByPlatform.Select(p => $"{p.Key}={p.Value}"));
                // Informational, not a warning: SponsorshipStart is a documented escape hatch and the consumer's build log is the only audit trail.
                log.LogMessage(
                    "SponsorCheck",
                    "SC008",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    MessageImportance.High,
                    $"Package '{s.PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {pd:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.");
                return true;
            }
        }

        // Fall through: enforce hash check.
        if (!File.Exists(sponsorHashListPath))
        {
            log.LogError(
                "SponsorCheck",
                "SC010",
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
        var checkAttempts = new List<string>();
        foreach (var pair in s.AccountByPlatform)
        {
            var hash = SponsorHasher.Hash(pair.Key, pair.Value);
            if (hashes.Contains(hash))
            {
                return true;
            }

            checkAttempts.Add($"{pair.Key}={pair.Value}");
        }

        var hint = string.IsNullOrWhiteSpace(s.SponsorshipStartRaw)
            ? " If you started sponsoring after this package was released, add SponsorshipStart=\"yyyy-MM-dd\" metadata."
            : "";
        log.LogError(
            "SponsorCheck",
            "SC004",
            "",
            "",
            0,
            0,
            0,
            0,
            $"Package '{s.PackageId}': no supplied sponsor account matches the bundled list (tried: {string.Join(", ", checkAttempts)}).{hint}");
        return false;
    }

    static bool ApplyLicensed(LicenseDecision.Licensed l, TaskLoggingHelper log, DateTime utcNow)
    {
        if (!TryParseYearMonth(l.LicensedUntilRaw, out var year, out var month))
        {
            log.LogError(
                "SponsorCheck",
                "SC007",
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
                "SponsorCheck",
                "SC005",
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

    static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

    static DateTime? TryReadPackDate(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path).Trim();
        return TryParseDate(text, out var date) ? date : null;
    }
}