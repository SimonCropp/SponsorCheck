public static class DecisionApplier
{
    const string newline = "\n";

    public static bool Apply(
        LicenseDecision decision,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        IReadOnlyList<AuthorAccount> authorAccounts,
        IReadOnlyDictionary<string, string> exemptionsDefined,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        switch (decision)
        {
            case LicenseDecision.MissingConfig missingConfig:
            {
                var (code, opener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC021", $"Package '{missingConfig.PackageId}' requires a SponsorCheck license property. '{missingConfig.PackageId}' is published in owner mode, so sponsorship is configured once via an MSBuild property (in Directory.Build.props or the consuming project)."),
                    ConsumerMode.Cpm => ("SC002", $"Package '{missingConfig.PackageId}' requires license metadata on the <PackageVersion> for '{missingConfig.PackageId}' in Directory.Packages.props."),
                    _ => ("SC001", $"Package '{missingConfig.PackageId}' requires license metadata on the <PackageReference> for '{missingConfig.PackageId}'.")
                };
                return SponsorCheckLog.Emit(
                    log,
                    code,
                    Severity.Error,
                    severityOverrides,
                    messageOverrides,
                    $"""
                     {opener}

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts, exemptionsDefined)}
                     """);
            }

            case LicenseDecision.ConflictingModes conflictingModes:
            {
                var (code, opener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC022", $"Package '{conflictingModes.PackageId}': mutually exclusive license properties are set ({string.Join(", ", conflictingModes.Modes)}). Pick one."),
                    ConsumerMode.Cpm => ("SC004", $"Package '{conflictingModes.PackageId}': mutually exclusive license modes are set on the <PackageVersion> in Directory.Packages.props ({string.Join(", ", conflictingModes.Modes)}). Pick one."),
                    _ => ("SC003", $"Package '{conflictingModes.PackageId}': mutually exclusive license modes are set on the <PackageReference> ({string.Join(", ", conflictingModes.Modes)}). Pick one.")
                };
                var editLine = context.IsOwner
                    ? $"Edit the SponsorCheck properties for '{conflictingModes.PackageId}' in Directory.Build.props or the consuming project."
                    : $"""
                       Edit the <{context.ElementName}> for '{conflictingModes.PackageId}' in:
                         {context.TargetFilePath}
                       """;
                var prefix = context.IsOwner ? $"{context.OwnerId}_" : "";
                var keepOneOf = string.Join(", ", ConsumerMetadataNames.All.Select(_ => $"{prefix}{_}"));
                // SponsorshipExemption only appears in the "keep one of" list when the publisher
                // has defined at least one — listing it for a package that doesn't offer any
                // would tease an option that has no valid value.
                var exemptionInList = exemptionsDefined.Count > 0
                    ? $"{prefix}SponsorshipExemption, "
                    : "";
                SponsorCheckLog.Error(
                    log,
                    code,
                    $"""
                     {opener}

                     {editLine}

                     Keep exactly one of: {keepOneOf}, {prefix}SponsorshipLicensedUntil, {exemptionInList}or {prefix}SponsorshipLicenseIgnored.
                     """);
                return false;
            }

            case LicenseDecision.Ignored ignored:
            {
                var (code, opener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC023", $"Package '{ignored.PackageId}': {context.OwnerId}_SponsorshipLicenseIgnored=\"true\" property is set. Build is allowed but is in breach of the package license."),
                    ConsumerMode.Cpm => ("SC006", $"Package '{ignored.PackageId}': SponsorshipLicenseIgnored=\"true\" on the <PackageVersion> in Directory.Packages.props. Build is allowed but is in breach of the package license."),
                    _ => ("SC005", $"Package '{ignored.PackageId}': SponsorshipLicenseIgnored=\"true\" on the <PackageReference>. Build is allowed but is in breach of the package license.")
                };
                return SponsorCheckLog.Emit(
                    log,
                    code,
                    Severity.Warning,
                    severityOverrides,
                    messageOverrides,
                    $"""
                     {opener}

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts, exemptionsDefined, includeIgnoreOption: false)}
                     """);
            }

            case LicenseDecision.Exempt exempt:
                return ApplyExempt(exempt, context, exemptionsDefined, severityOverrides, messageOverrides, log);

            case LicenseDecision.Sponsor sponsor:
                return ApplySponsor(sponsor, sponsorHashListPath, packDatePath, context, authorAccounts, severityOverrides, messageOverrides, log, utcNow);

            case LicenseDecision.Licensed licensed:
                return ApplyLicensed(licensed, context, authorAccounts, severityOverrides, messageOverrides, log, utcNow);

            default:
                throw new InvalidOperationException($"Unknown decision: {decision.GetType().Name}");
        }
    }

    static bool ApplyExempt(
        LicenseDecision.Exempt exempt,
        ConsumerContext context,
        IReadOnlyDictionary<string, string> exemptionsDefined,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log)
    {
        // Lookup is case-insensitive (the loaded dict uses OrdinalIgnoreCase) but the warning
        // body surfaces what the consumer actually typed — that's the audit signal in CI logs.
        if (!exemptionsDefined.TryGetValue(exempt.ExemptionName, out var publisherMessage))
        {
            var (unknownCode, unknownOpener) = context.Mode switch
            {
                ConsumerMode.Owner => ("SC034", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemption=\"{exempt.ExemptionName}\" does not name a known exemption."),
                ConsumerMode.Cpm => ("SC033", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageVersion> in Directory.Packages.props does not name a known exemption."),
                _ => ("SC032", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageReference> does not name a known exemption.")
            };
            SponsorCheckLog.Error(
                log,
                unknownCode,
                $"""
                 {unknownOpener}

                 {ConsumerMetadataExamples.RenderAvailableExemptions(context, exemptionsDefined)}
                 """);
            return false;
        }

        // Known name path: the message body IS the publisher's criteria text — no remediation
        // block, no re-listing of license-mode options. Surfaces the audit trail directly.
        var (code, opener) = context.Mode switch
        {
            ConsumerMode.Owner => ("SC031", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemption=\"{exempt.ExemptionName}\" property is set. Publisher's exemption criteria: {publisherMessage}"),
            ConsumerMode.Cpm => ("SC030", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" claimed on the <PackageVersion> in Directory.Packages.props. Publisher's exemption criteria: {publisherMessage}"),
            _ => ("SC029", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" claimed on the <PackageReference>. Publisher's exemption criteria: {publisherMessage}")
        };
        return SponsorCheckLog.Emit(
            log,
            code,
            Severity.Warning,
            severityOverrides,
            messageOverrides,
            opener);
    }

    static bool ApplySponsor(
        LicenseDecision.Sponsor sponsor,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        IReadOnlyList<AuthorAccount> authorAccounts,
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
                var (startFormatCode, startFormatOpener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC027", $"Package '{sponsor.PackageId}': {context.OwnerId}_SponsorshipStart='{sponsor.SponsorshipStartRaw}' property is not in 'yyyy-MM-dd' format."),
                    ConsumerMode.Cpm => ("SC014", $"Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM-dd' format."),
                    _ => ("SC013", $"Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' on the <PackageReference> is not in 'yyyy-MM-dd' format.")
                };
                SponsorCheckLog.Error(
                    log,
                    startFormatCode,
                    $"""
                     {startFormatOpener}

                     {ConsumerMetadataExamples.RenderSponsorshipStartFix(context)}
                     """);
                return false;
            }

            if (startDate > utcNow.Date)
            {
                var (futureStartCode, futureStartOpener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC028", $"Package '{sponsor.PackageId}': {context.OwnerId}_SponsorshipStart='{sponsor.SponsorshipStartRaw}' property is in the future."),
                    ConsumerMode.Cpm => ("SC016", $"Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' on the <PackageVersion> in Directory.Packages.props is in the future."),
                    _ => ("SC015", $"Package '{sponsor.PackageId}': SponsorshipStart='{sponsor.SponsorshipStartRaw}' on the <PackageReference> is in the future.")
                };
                SponsorCheckLog.Error(
                    log,
                    futureStartCode,
                    $"""
                     {futureStartOpener}

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
                    "SC017",
                    $"Package '{sponsor.PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {pd:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.");
                return true;
            }
        }

        // Fall through: enforce hash check.
        if (!File.Exists(sponsorHashListPath))
        {
            SponsorCheckLog.Error(
                log,
                "SC018",
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

        var (code, opener) = context.Mode switch
        {
            ConsumerMode.Owner => ("SC024", $"Package '{sponsor.PackageId}': no sponsor account property matches the bundled list."),
            ConsumerMode.Cpm => ("SC008", $"Package '{sponsor.PackageId}': no sponsor account declared on the <PackageVersion> in Directory.Packages.props matches the bundled list."),
            _ => ("SC007", $"Package '{sponsor.PackageId}': no sponsor account declared on the <PackageReference> matches the bundled list.")
        };
        var lines = new List<string>
        {
            opener,
            "",
            $"Tried: {string.Join(", ", checkAttempts)}"
        };
        var sponsorAt = ConsumerMetadataExamples.RenderSponsorAtBlock(authorAccounts);
        if (sponsorAt.Length > 0)
        {
            lines.Add("");
            lines.Add(sponsorAt);
        }

        if (string.IsNullOrWhiteSpace(sponsor.SponsorshipStartRaw))
        {
            lines.Add("");
            lines.Add(ConsumerMetadataExamples.RenderSponsorshipStartHint(context, sponsor.AccountByPlatform));
        }

        return SponsorCheckLog.Emit(
            log,
            code,
            Severity.Error,
            severityOverrides,
            messageOverrides,
            string.Join(newline, lines));
    }

    static bool ApplyLicensed(
        LicenseDecision.Licensed l,
        ConsumerContext context,
        IReadOnlyList<AuthorAccount> authorAccounts,
        IReadOnlyDictionary<string, Severity> severityOverrides,
        IReadOnlyDictionary<string, string> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        if (!TryParseYearMonth(l.LicensedUntilRaw, out var year, out var month))
        {
            var (code, opener) = context.Mode switch
            {
                ConsumerMode.Owner => ("SC026", $"Package '{l.PackageId}': {context.OwnerId}_SponsorshipLicensedUntil='{l.LicensedUntilRaw}' property is not in 'yyyy-MM' format."),
                ConsumerMode.Cpm => ("SC012", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM' format."),
                _ => ("SC011", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageReference> is not in 'yyyy-MM' format.")
            };
            SponsorCheckLog.Error(
                log,
                code,
                $"""
                 {opener}

                 {ConsumerMetadataExamples.RenderLicensedUntilFormatFix(context)}
                 """);
            return false;
        }

        // Expiry is decided at month granularity by comparing (year, month) directly rather than
        // materializing "start of next month". A build in any month at or before the licensed month
        // passes — including the final fractional second of the last day, with no whole-second edge —
        // while the first day of the next month is the cutoff. Comparing the calendar fields also
        // avoids overflowing DateTime.MaxValue for the perpetual sentinel SponsorshipLicensedUntil=
        // "9999-12": AddMonths(1) there throws ArgumentOutOfRangeException, which would otherwise
        // surface as a code-less build error instead of passing.
        var expired = utcNow.Year > year ||
                      (utcNow.Year == year && utcNow.Month > month);
        if (expired)
        {
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
            var (code, opener) = context.Mode switch
            {
                ConsumerMode.Owner => ("SC025", $"Package '{l.PackageId}': {context.OwnerId}_SponsorshipLicensedUntil='{l.LicensedUntilRaw}' property has expired (end of month {lastDay:yyyy-MM-dd} UTC)."),
                ConsumerMode.Cpm => ("SC010", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageVersion> in Directory.Packages.props has expired (end of month {lastDay:yyyy-MM-dd} UTC)."),
                _ => ("SC009", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageReference> has expired (end of month {lastDay:yyyy-MM-dd} UTC).")
            };
            var expiredLines = new List<string>
            {
                opener,
                "",
                ConsumerMetadataExamples.RenderLicensedUntilRenewal(context)
            };
            var expiredSponsorAt = ConsumerMetadataExamples.RenderSponsorAtBlock(authorAccounts);
            if (expiredSponsorAt.Length > 0)
            {
                expiredLines.Add("");
                expiredLines.Add(expiredSponsorAt);
            }

            return SponsorCheckLog.Emit(
                log,
                code,
                Severity.Error,
                severityOverrides,
                messageOverrides,
                string.Join(newline, expiredLines));
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
