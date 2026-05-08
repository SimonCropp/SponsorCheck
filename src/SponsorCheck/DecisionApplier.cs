public static class DecisionApplier
{
    public static bool Apply(
        LicenseDecision decision,
        string sponsorHashListPath,
        string packDatePath,
        IReadOnlyList<string> sponsorUrls,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        switch (decision)
        {
            case LicenseDecision.MissingConfig m:
                return SponsorCheckLog.Emit(
                    log,
                    "SC001",
                    Severity.Error,
                    severityOverrides,
                    messageOverrides,
                    $"Package '{m.PackageId}' is built with SponsorCheck and requires one license-mode metadata: SponsorshipLicenseIgnored=\"true\", a <Platform>SponsorAccount, or SponsorshipLicensedUntil=\"yyyy-MM\". Sponsor at: {string.Join(", ", sponsorUrls)}.");

            case LicenseDecision.ConflictingModes c:
                SponsorCheckLog.Error(
                    log,
                    "SC002",
                    $"Package '{c.PackageId}': mutually exclusive license modes set ({string.Join(", ", c.Modes)}). Pick one.");
                return false;

            case LicenseDecision.Ignored i:
                return SponsorCheckLog.Emit(
                    log,
                    "SC003",
                    Severity.Warning,
                    severityOverrides,
                    messageOverrides,
                    $"Package '{i.PackageId}': SponsorshipLicenseIgnored=\"true\". Build is allowed but is in breach of the license of the package. Sponsor at: {string.Join(", ", sponsorUrls)}.");

            case LicenseDecision.Sponsor s:
                return ApplySponsor(s, sponsorHashListPath, packDatePath, severityOverrides, messageOverrides, log, utcNow);

            case LicenseDecision.Licensed l:
                return ApplyLicensed(l, severityOverrides, messageOverrides, log, utcNow);

            default:
                throw new InvalidOperationException($"Unknown decision: {decision.GetType().Name}");
        }
    }

    static bool ApplySponsor(
        LicenseDecision.Sponsor s,
        string sponsorHashListPath,
        string packDatePath,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        // If consumer declared SponsorshipStart, see if they signed up after the package was packed.
        // If so, the bundled hash couldn't possibly know about them — trust the declaration.
        if (!string.IsNullOrWhiteSpace(s.SponsorshipStartRaw))
        {
            if (!TryParseDate(s.SponsorshipStartRaw!, out var startDate))
            {
                SponsorCheckLog.Error(
                    log,
                    "SC010",
                    $"Package '{s.PackageId}': SponsorshipStart='{s.SponsorshipStartRaw}' is not in 'yyyy-MM-dd' format.");
                return false;
            }

            if (startDate > utcNow.Date)
            {
                SponsorCheckLog.Error(
                    log,
                    "SC011",
                    $"Package '{s.PackageId}': SponsorshipStart='{s.SponsorshipStartRaw}' is in the future.");
                return false;
            }

            var packDate = TryReadPackDate(packDatePath);
            if (packDate is { } pd &&
                startDate > pd)
            {
                var attempts = string.Join(", ", s.AccountByPlatform.Select(_ => $"{_.Key}={_.Value}"));
                // Informational, not a warning: SponsorshipStart is a documented escape hatch and the consumer's build log is the only audit trail.
                SponsorCheckLog.HighMessage(
                    log,
                    "SC008",
                    $"Package '{s.PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {pd:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.");
                return true;
            }
        }

        // Fall through: enforce hash check.
        if (!File.Exists(sponsorHashListPath))
        {
            SponsorCheckLog.Error(
                log,
                "SC009",
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
            ? " If sponsorship started after this package was released, add SponsorshipStart=\"yyyy-MM-dd\" metadata."
            : "";
        return SponsorCheckLog.Emit(
            log,
            "SC004",
            Severity.Error,
            severityOverrides,
            messageOverrides,
            $"Package '{s.PackageId}': no supplied sponsor account matches the bundled list (tried: {string.Join(", ", checkAttempts)}).{hint}");
    }

    static bool ApplyLicensed(
        LicenseDecision.Licensed l,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        if (!TryParseYearMonth(l.LicensedUntilRaw, out var year, out var month))
        {
            SponsorCheckLog.Error(
                log,
                "SC007",
                $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' is not in 'yyyy-MM' format.");
            return false;
        }

        // Cutoff is the start of the next month: a build at any instant within the licensed
        // month — including the final fractional second — must still pass. Using the last day
        // at 23:59:59 (whole-second precision) would incorrectly flag builds in the last second.
        var startOfNextMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        if (utcNow >= startOfNextMonth)
        {
            var lastDay = startOfNextMonth.AddDays(-1);
            return SponsorCheckLog.Emit(
                log,
                "SC005",
                Severity.Error,
                severityOverrides,
                messageOverrides,
                $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' has expired (end of month {lastDay:yyyy-MM-dd} UTC).");
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
