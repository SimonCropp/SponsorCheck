public static class DecisionApplier
{
    const char newline = '\n';

    public static bool Apply(
        LicenseDecision decision,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        // Lazy: these back only diagnostic rendering (and the exemption lookup). The happy paths —
        // sponsor account matches, or license still valid — return without forcing any of them, so a
        // correctly configured consumer build reads none of the four backing sidecar files. Each is
        // forced with .Value only at the point a branch needs it.
        Lazy<IReadOnlyList<AuthorAccount>> authorAccounts,
        Lazy<IReadOnlyDictionary<string, ExemptionDefinition>> exemptionsDefined,
        Lazy<IReadOnlyDictionary<string, Severity>> severityOverrides,
        Lazy<IReadOnlyDictionary<string, string>> messageOverrides,
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
                    severityOverrides.Value,
                    messageOverrides.Value,
                    $"""
                     {opener}

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts.Value, exemptionsDefined.Value)}
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
                var exemptionInList = exemptionsDefined.Value.Count > 0
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
                    severityOverrides.Value,
                    messageOverrides.Value,
                    $"""
                     {opener}

                     {ConsumerMetadataExamples.RenderLicenseModeOptions(context, authorAccounts.Value, exemptionsDefined.Value, includeIgnoreOption: false)}
                     """);
            }

            case LicenseDecision.Exempt exempt:
                return ApplyExempt(exempt, context, exemptionsDefined, severityOverrides, messageOverrides, log, utcNow);

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
        Lazy<IReadOnlyDictionary<string, ExemptionDefinition>> exemptionsDefined,
        Lazy<IReadOnlyDictionary<string, Severity>> severityOverrides,
        Lazy<IReadOnlyDictionary<string, string>> messageOverrides,
        TaskLoggingHelper log,
        DateTime utcNow)
    {
        // Lookup is case-insensitive (the loaded dict uses OrdinalIgnoreCase) but the warning
        // body surfaces what the consumer actually typed — that's the audit signal in CI logs.
        if (!exemptionsDefined.Value.TryGetValue(exempt.ExemptionName, out var definition))
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

                 {ConsumerMetadataExamples.RenderAvailableExemptions(context, exemptionsDefined.Value)}
                 """);
            return false;
        }

        var maxTermMonths = definition!.MaxTermMonths;
        // The publisher's cap is measured from the build clock, not from when the consumer wrote
        // the value, so the ceiling rolls forward with time. That is what makes an already-valid
        // claim stay valid while still forcing a deliberate re-attestation every {max} months.
        // Computed once so the month the SC044 check compares against and the month its message
        // names can't drift apart.
        var ceilingMonth = maxTermMonths is { } months ? AddMonths(utcNow, months) : ((int, int)?) null;
        var ceiling = ceilingMonth is { } value ? RenderMonth(value) : null;

        if (string.IsNullOrWhiteSpace(exempt.ExemptionUntilRaw))
        {
            // Only a capped exemption demands an end month. An uncapped one may be claimed
            // open-endedly, exactly as it could before MaxTermMonths existed.
            if (maxTermMonths is { } required)
            {
                var (missingCode, missingOpener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC040", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemption=\"{exempt.ExemptionName}\" property is set, but the publisher time-bounds this exemption to {MonthsWord(required)}, so {context.OwnerId}_SponsorshipExemptionUntil must be set too."),
                    ConsumerMode.Cpm => ("SC039", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageVersion> in Directory.Packages.props is missing SponsorshipExemptionUntil — the publisher time-bounds this exemption to {MonthsWord(required)}."),
                    _ => ("SC038", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageReference> is missing SponsorshipExemptionUntil — the publisher time-bounds this exemption to {MonthsWord(required)}.")
                };
                SponsorCheckLog.Error(
                    log,
                    missingCode,
                    $"""
                     {missingOpener}

                     {ConsumerMetadataExamples.RenderExemptionUntilFix(context, exempt.ExemptionName, ceiling!)}
                     """);
                return false;
            }
        }
        else if (!TryParseYearMonth(exempt.ExemptionUntilRaw!, out var untilYear, out var untilMonth))
        {
            var (formatCode, formatOpener) = context.Mode switch
            {
                ConsumerMode.Owner => ("SC043", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' property is not in 'yyyy-MM' format."),
                ConsumerMode.Cpm => ("SC042", $"Package '{exempt.PackageId}': SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM' format."),
                _ => ("SC041", $"Package '{exempt.PackageId}': SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' on the <PackageReference> is not in 'yyyy-MM' format.")
            };
            SponsorCheckLog.Error(
                log,
                formatCode,
                $"""
                 {formatOpener}

                 {ConsumerMetadataExamples.RenderExemptionUntilFormatFix(context, exempt.ExemptionName)}
                 """);
            return false;
        }
        else
        {
            // Beyond-ceiling and expired are mutually exclusive (one is ahead of the cap, the
            // other behind today), so the order between them never changes which code fires.
            if (maxTermMonths is { } max &&
                ceilingMonth is { } cap &&
                IsAfter(untilYear, untilMonth, cap))
            {
                var (maxCode, maxOpener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC046", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' property is more than {MonthsWord(max)} in the future (maximum {ceiling})."),
                    ConsumerMode.Cpm => ("SC045", $"Package '{exempt.PackageId}': SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' on the <PackageVersion> in Directory.Packages.props is more than {MonthsWord(max)} in the future (maximum {ceiling})."),
                    _ => ("SC044", $"Package '{exempt.PackageId}': SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' on the <PackageReference> is more than {MonthsWord(max)} in the future (maximum {ceiling}).")
                };
                SponsorCheckLog.Error(
                    log,
                    maxCode,
                    $"""
                     {maxOpener}

                     {ConsumerMetadataExamples.RenderExemptionUntilFix(context, exempt.ExemptionName, ceiling!)}
                     """);
                return false;
            }

            // Same month-granularity expiry as SponsorshipLicensedUntil: the named month is fully
            // covered, and the first day of the following month is the cutoff.
            if (IsAfter(utcNow.Year, utcNow.Month, (untilYear, untilMonth)))
            {
                var lastDay = new DateTime(untilYear, untilMonth, DateTime.DaysInMonth(untilYear, untilMonth), 0, 0, 0, DateTimeKind.Utc);
                var (expiredCode, expiredOpener) = context.Mode switch
                {
                    ConsumerMode.Owner => ("SC049", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemption=\"{exempt.ExemptionName}\" expired — {context.OwnerId}_SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' (end of month {lastDay:yyyy-MM-dd} UTC)."),
                    ConsumerMode.Cpm => ("SC048", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageVersion> in Directory.Packages.props expired — SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' (end of month {lastDay:yyyy-MM-dd} UTC)."),
                    _ => ("SC047", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" on the <PackageReference> expired — SponsorshipExemptionUntil='{exempt.ExemptionUntilRaw}' (end of month {lastDay:yyyy-MM-dd} UTC).")
                };
                SponsorCheckLog.Error(
                    log,
                    expiredCode,
                    $"""
                     {expiredOpener}

                     {ConsumerMetadataExamples.RenderExemptionUntilRenewal(context, exempt.ExemptionName, ceiling)}
                     """);
                return false;
            }
        }

        // Known name path: the message body IS the publisher's criteria text — no remediation
        // block, no re-listing of license-mode options. Surfaces the audit trail directly. When
        // the claim is time-bounded the end month rides along in the same line, so the CI log
        // records not just which carve-out was claimed but how long it was claimed for.
        var bound = string.IsNullOrWhiteSpace(exempt.ExemptionUntilRaw)
            ? ""
            : $" until {exempt.ExemptionUntilRaw}";
        var (code, opener) = context.Mode switch
        {
            ConsumerMode.Owner => ("SC031", $"Package '{exempt.PackageId}': {context.OwnerId}_SponsorshipExemption=\"{exempt.ExemptionName}\" property is set{bound}. Publisher's exemption criteria: {definition.Message}"),
            ConsumerMode.Cpm => ("SC030", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" claimed on the <PackageVersion> in Directory.Packages.props{bound}. Publisher's exemption criteria: {definition.Message}"),
            _ => ("SC029", $"Package '{exempt.PackageId}': SponsorshipExemption=\"{exempt.ExemptionName}\" claimed on the <PackageReference>{bound}. Publisher's exemption criteria: {definition.Message}")
        };
        return SponsorCheckLog.Emit(
            log,
            code,
            Severity.Warning,
            severityOverrides.Value,
            messageOverrides.Value,
            opener);
    }

    static bool ApplySponsor(
        LicenseDecision.Sponsor sponsor,
        string sponsorHashListPath,
        string packDatePath,
        ConsumerContext context,
        Lazy<IReadOnlyList<AuthorAccount>> authorAccounts,
        Lazy<IReadOnlyDictionary<string, Severity>> severityOverrides,
        Lazy<IReadOnlyDictionary<string, string>> messageOverrides,
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

        // Compute the consumer's candidate hashes up front — at most one per configured platform (≤3) —
        // then stream the bundled list and return on the first match. The verifier runs on every consumer
        // build in every configuration, so this is the product's hottest path: streaming with File.ReadLines
        // avoids materializing the whole file into a string[] plus HashSet, and short-circuits as soon as a
        // matching line is found instead of always reading the entire list.
        var candidateHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in sponsor.AccountByPlatform)
        {
            candidateHashes.Add(SponsorHasher.Hash(pair.Key, pair.Value));
        }

        foreach (var bundledHash in File.ReadLines(sponsorHashListPath))
        {
            if (candidateHashes.Contains(bundledHash))
            {
                return true;
            }
        }

        var (code, opener) = context.Mode switch
        {
            ConsumerMode.Owner => ("SC024", $"Package '{sponsor.PackageId}': no sponsor account property matches the bundled list."),
            ConsumerMode.Cpm => ("SC008", $"Package '{sponsor.PackageId}': no sponsor account declared on the <PackageVersion> in Directory.Packages.props matches the bundled list."),
            _ => ("SC007", $"Package '{sponsor.PackageId}': no sponsor account declared on the <PackageReference> matches the bundled list.")
        };
        // Same ordering as the candidate loop above (AccountByPlatform iteration order), so the
        // "Tried:" audit line in the SC007/SC008/SC024 message is unchanged. Built only on the
        // failure path — the success path never allocates it.
        var checkAttempts = sponsor.AccountByPlatform.Select(_ => $"{_.Key}={_.Value}");
        var lines = new List<string>
        {
            opener,
            "",
            $"Tried: {string.Join(", ", checkAttempts)}"
        };
        var sponsorAt = ConsumerMetadataExamples.RenderSponsorAtBlock(authorAccounts.Value);
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
            severityOverrides.Value,
            messageOverrides.Value,
            string.Join(newline, lines));
    }

    static bool ApplyLicensed(
        LicenseDecision.Licensed l,
        ConsumerContext context,
        Lazy<IReadOnlyList<AuthorAccount>> authorAccounts,
        Lazy<IReadOnlyDictionary<string, Severity>> severityOverrides,
        Lazy<IReadOnlyDictionary<string, string>> messageOverrides,
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

        // SponsorshipLicensedUntil is an unverified self-attestation, so it is capped at one year
        // out — otherwise "9999-12" would be a permanent opt-out dressed up as a license. The cap
        // forces the consumer to re-affirm the arrangement each year. Compared on calendar fields
        // rather than utcNow.AddYears(1) so a build in year 9999 doesn't overflow DateTime.MaxValue.
        var maxYear = utcNow.Year + 1;
        if (year > maxYear ||
            (year == maxYear && month > utcNow.Month))
        {
            var maxMonth = $"{maxYear:0000}-{utcNow.Month:00}";
            var (code, opener) = context.Mode switch
            {
                ConsumerMode.Owner => ("SC037", $"Package '{l.PackageId}': {context.OwnerId}_SponsorshipLicensedUntil='{l.LicensedUntilRaw}' property is more than 1 year in the future (maximum {maxMonth})."),
                ConsumerMode.Cpm => ("SC036", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageVersion> in Directory.Packages.props is more than 1 year in the future (maximum {maxMonth})."),
                _ => ("SC035", $"Package '{l.PackageId}': SponsorshipLicensedUntil='{l.LicensedUntilRaw}' on the <PackageReference> is more than 1 year in the future (maximum {maxMonth}).")
            };
            SponsorCheckLog.Error(
                log,
                code,
                $"""
                 {opener}

                 {ConsumerMetadataExamples.RenderLicensedUntilMaxFix(context, maxMonth)}
                 """);
            return false;
        }

        // Expiry is decided at month granularity by comparing (year, month) directly rather than
        // materializing "start of next month". A build in any month at or before the licensed month
        // passes — including the final fractional second of the last day, with no whole-second edge —
        // while the first day of the next month is the cutoff. Comparing the calendar fields also
        // avoids overflowing DateTime.MaxValue at the calendar extreme, where the cap above still
        // admits "9999-12": AddMonths(1) there throws ArgumentOutOfRangeException, which would
        // otherwise surface as a code-less build error instead of passing.
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
            var expiredSponsorAt = ConsumerMetadataExamples.RenderSponsorAtBlock(authorAccounts.Value);
            if (expiredSponsorAt.Length > 0)
            {
                expiredLines.Add("");
                expiredLines.Add(expiredSponsorAt);
            }

            return SponsorCheckLog.Emit(
                log,
                code,
                Severity.Error,
                severityOverrides.Value,
                messageOverrides.Value,
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

    // Month arithmetic on the calendar fields rather than DateTime.AddMonths: a large MaxTermMonths
    // in a build late in year 9999 would overflow DateTime.MaxValue, and the result here is only
    // ever compared against another (year, month) pair or formatted, never materialized as a date.
    static (int Year, int Month) AddMonths(DateTime utcNow, int months)
    {
        var total = (utcNow.Year * 12) + (utcNow.Month - 1) + months;
        return (total / 12, (total % 12) + 1);
    }

    static bool IsAfter(int year, int month, (int Year, int Month) other) =>
        year > other.Year ||
        (year == other.Year && month > other.Month);

    static string RenderMonth((int Year, int Month) month) =>
        $"{month.Year:0000}-{month.Month:00}";

    static string MonthsWord(int months) =>
        months == 1 ? "1 month" : $"{months} months";

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
