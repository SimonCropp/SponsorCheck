public static class DecisionApplier
{
    const string newline = "\n";

    public static bool Apply(
        LicenseDecision decision,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        IReadOnlyList<AuthorAccount> authorAccounts,
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
                    $"""
                     Package '{m.PackageId}' is built with SponsorCheck and requires license metadata applied to the {context.ElementName}.

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts)}
                     """);

            case LicenseDecision.ConflictingModes c:
                SponsorCheckLog.Error(
                    log,
                    "SC002",
                    $"""
                     Package '{c.PackageId}': mutually exclusive license modes are set ({string.Join(", ", c.Modes)}). Pick one.

                     Edit the <{context.ElementName}> for '{c.PackageId}' in:
                       {context.TargetFilePath}

                     Keep exactly one of: SponsorshipLicenseIgnored, a <Platform>SponsorAccount, or SponsorshipLicensedUntil.
                     """);
                return false;

            case LicenseDecision.Ignored i:
                return SponsorCheckLog.Emit(
                    log,
                    "SC003",
                    Severity.Warning,
                    severityOverrides,
                    messageOverrides,
                    $"""
                     Package '{i.PackageId}': SponsorshipLicenseIgnored="true". Build is allowed but is in breach of the package license.

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts)}
                     """);

            case LicenseDecision.Sponsor s:
                return ApplySponsor(s, sponsorHashListPath, packDatePath, context, severityOverrides, messageOverrides, log, utcNow);

            case LicenseDecision.Licensed l:
                return ApplyLicensed(l, context, severityOverrides, messageOverrides, log, utcNow);

            default:
                throw new InvalidOperationException($"Unknown decision: {decision.GetType().Name}");
        }
    }

    static bool ApplySponsor(
        LicenseDecision.Sponsor sponsor,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        // If consumer declared SponsorshipStart, see if they signed up after the package was packed.
        // If so, the bundled hash couldn't possibly know about them — trust the declaration.
        if (!string.IsNullOrWhiteSpace(sponsor.SponsorshipStartRaw))
        {
            if (!TryParseDate(sponsor.SponsorshipStartRaw!, out var startDate))
            {
                SponsorCheckLog.Error(
                    log,
                    "SC010",
                    $"""
                     Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' is not in 'yyyy-MM-dd' format.

                     {ConsumerMetadataExamples.RenderSponsorshipStartFix(context)}
                     """);
                return false;
            }

            if (startDate > utcNow.Date)
            {
                SponsorCheckLog.Error(
                    log,
                    "SC011",
                    $"""
                     Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' is in the future.

                     {ConsumerMetadataExamples.RenderSponsorshipStartFix(context)}
                     """);
                return false;
            }

            var packDate = TryReadPackDate(packDatePath);
            if (packDate is { } pd &&
                startDate > pd)
            {
                var attempts = string.Join(", ", sponsor.AccountByPlatform.Select(_ => $"{_.Key}={_.Value}"));
                // Informational, not a warning: SponsorshipStart is a documented escape hatch and the consumer's build log is the only audit trail.
                SponsorCheckLog.HighMessage(
                    log,
                    "SC008",
                    $"Package '{sponsor.PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {pd:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.");
                return true;
            }
        }

        // Fall through: enforce hash check.
        if (!File.Exists(sponsorHashListPath))
        {
            SponsorCheckLog.Error(
                log,
                "SC009",
                $"Package '{sponsor.PackageId}': bundled sponsor hash file not found at '{sponsorHashListPath}'.");
            return false;
        }

        var hashes = new HashSet<string>(File.ReadAllLines(sponsorHashListPath), StringComparer.Ordinal);
        var checkAttempts = new List<string>();
        foreach (var pair in sponsor.AccountByPlatform)
        {
            var hash = SponsorHasher.Hash(pair.Key, pair.Value);
            if (hashes.Contains(hash))
            {
                return true;
            }

            checkAttempts.Add($"{pair.Key}={pair.Value}");
        }

        var lines = new List<string>
        {
            $"Package '{sponsor.PackageId}': no supplied sponsor account matches the bundled list.",
            "",
            $"Tried: {string.Join(", ", checkAttempts)}"
        };
        if (string.IsNullOrWhiteSpace(sponsor.SponsorshipStartRaw))
        {
            lines.Add("");
            lines.Add(ConsumerMetadataExamples.RenderSponsorshipStartHint(context, sponsor.AccountByPlatform));
        }

        return SponsorCheckLog.Emit(
            log,
            "SC004",
            Severity.Error,
            severityOverrides,
            messageOverrides,
            string.Join(newline, lines));
    }

    static bool ApplyLicensed(
        LicenseDecision.Licensed l,
        ConsumerContext context,
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
                $"""
                 Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' is not in 'yyyy-MM' format.

                 {ConsumerMetadataExamples.RenderLicensedUntilFormatFix(context)}
                 """);
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
                $"""
                 Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' has expired (end of month {lastDay:yyyy-MM-dd} UTC).

                 {ConsumerMetadataExamples.RenderLicensedUntilRenewal(context)}
                 """);
        }

        return true;
    }

    static bool TryParseYearMonth(string value, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }

    static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);

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
