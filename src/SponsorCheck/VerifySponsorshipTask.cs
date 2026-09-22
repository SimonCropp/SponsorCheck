// Not sealed: the build generates a version-scoped subclass (VerifySponsorshipTask_<version>, see
// _SponsorCheck_GenerateVersionedTaskName in SponsorCheck.csproj) and the shipped verifier targets
// UsingTask *that* name. MSBuild's task registry is keyed by task name alone, so a bare shared name
// lets whichever package registers first supply the task for every SponsorCheck-bundling package in
// the project — MSB4064 the moment their task APIs differ.
public class VerifySponsorshipTask :
    Microsoft.Build.Utilities.Task
{
    [Required]
    public string ThePackageId { get; set; } = "";

    [Required]
    public string SponsorHashListPath { get; set; } = "";

    [Required]
    public string PackDatePath { get; set; } = "";

    [Required]
    public string AuthorAccountsPath { get; set; } = "";

    public string SeverityOverridesPath { get; set; } = "";
    public string MessageOverridesPath { get; set; } = "";
    public string LandingUrlPath { get; set; } = "";
    public string ExemptionsPath { get; set; } = "";

    // Splits the run in two. With DeferDiagnostic the verification happens as usual but whatever it
    // would have logged is handed back through the Diagnostic* outputs instead, for the generated
    // targets to announce once per build rather than once per project and target framework. The
    // Announce* inputs are the other half of that round trip: they carry one captured diagnostic
    // back in to be logged. Both default off, so a direct caller still gets an immediate verifier.
    public bool DeferDiagnostic { get; set; }
    public string AnnounceCode { get; set; } = "";
    public string AnnounceSeverity { get; set; } = "";
    public string AnnounceImportance { get; set; } = "";
    public string AnnouncePayload { get; set; } = "";

    // The consumer's SponsorCheckMessageLevel and SponsorCheckWarningLevel — see LogLevels. Empty keeps
    // the defaults.
    public string MessageLevel { get; set; } = "";
    public string WarningLevel { get; set; } = "";

    [Output]
    public string DiagnosticCode { get; set; } = "";

    // Already resolved against the consumer's log levels, so the announce run logs what it is handed
    // and needs none of the consumer's properties. The importance is set for a message only.
    [Output]
    public string DiagnosticSeverity { get; set; } = "";

    [Output]
    public string DiagnosticImportance { get; set; } = "";

    [Output]
    public string DiagnosticPayload { get; set; } = "";

    public string IsCpm { get; set; } = "";

    // Non-empty signals owner mode: the consumer configures sponsorship via global MSBuild
    // properties (passed through the *FromRef parameters) rather than per-package item metadata.
    public string OwnerId { get; set; } = "";
    public string ConsumerProjectPath { get; set; } = "";
    public string DirectoryPackagesPropsPath { get; set; } = "";

    public string PackageVersionFromRef { get; set; } = "";
    public string PackageVersionFromVer { get; set; } = "";

    public string IgnoredFromRef { get; set; } = "";
    public string IgnoredFromVer { get; set; } = "";
    public string LicensedUntilFromRef { get; set; } = "";
    public string LicensedUntilFromVer { get; set; } = "";
    public string SponsorshipExemptionFromRef { get; set; } = "";
    public string SponsorshipExemptionFromVer { get; set; } = "";
    public string SponsorshipExemptionUntilFromRef { get; set; } = "";
    public string SponsorshipExemptionUntilFromVer { get; set; } = "";
    public string SponsorshipStartFromRef { get; set; } = "";
    public string SponsorshipStartFromVer { get; set; } = "";
    public string SponsorshipPrivateUntilFromRef { get; set; } = "";

    public string SponsorshipPrivateUntilFromVer { get; set; } = "";

    // The publisher's cap on a SponsorshipPrivateUntil claim, substituted into the generated
    // verifier targets at pack time. Empty (or unparseable) means the packed targets predate the
    // setting, so fall back to the documented default rather than failing the consumer's build for
    // something only the publisher can fix.
    public string PrivateSponsorMaxTermMonths { get; set; } = "";
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
            if (AnnounceCode.Trim().Length > 0)
            {
                return Announce();
            }

            // Both paths render into a capture first, so the consumer's log levels are applied in one
            // place, to the finished diagnostic, whether it is then logged here or handed back. The
            // levels are checked before verifying: a typo in either would otherwise go unnoticed on
            // every build that has nothing to report.
            var capture = new CapturingBuildEngine();
            var log = new TaskLoggingHelper(capture, nameof(VerifySponsorshipTask));
            var passed = TryResolveLogLevels(log, out var levels) &&
                         Verify(log);
            if (capture.Diagnostic is not { } diagnostic)
            {
                return passed;
            }

            var (severity, importance) = levels.Apply(diagnostic.Severity, BuildServerDetector.Detected);
            if (!DeferDiagnostic)
            {
                SponsorCheckLog.EmitRendered(Log, diagnostic.Code, severity, importance, diagnostic.Message);
                return severity != Severity.Error;
            }

            DiagnosticCode = diagnostic.Code;
            DiagnosticSeverity = severity.ToString().ToLowerInvariant();
            if (severity == Severity.Message)
            {
                DiagnosticImportance = importance.ToString().ToLowerInvariant();
            }

            DiagnosticPayload = diagnostic.Encode();
            // Whether the build lives or dies is the announce run's call — it is the run that logs
            // the error, and MSBuild requires the task that returns false to be the one that logged
            // one. The <MSBuild> call driving it fails this project in turn.
            return true;
        }
        // Not routed through the deferral: an exception here is a SponsorCheck bug rather than a
        // consumer-side diagnostic, and naming the project that hit it is the point.
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    bool Announce()
    {
        // The severity round-trips through targets this task generated, so a value that doesn't
        // parse means the property was mangled in transit. Severity.Error is the default, which
        // fails closed rather than quietly downgrading an error to a message.
        SeverityOverrideFile.TryParseSeverity(AnnounceSeverity, out var severity);
        SponsorCheckLog.EmitRendered(Log, AnnounceCode.Trim(), severity, ResolveAnnounceImportance(), DeferredDiagnostic.Decode(AnnouncePayload));
        return severity != Severity.Error;
    }

    // Travels as the name of a level, as the verify run resolved it. Anything else means the property
    // was mangled in transit, and the build-server default is what the message would have had anyway
    // unless the consumer asked otherwise.
    MessageImportance ResolveAnnounceImportance()
    {
        LogLevels.TryParseLevel(AnnounceImportance, out var level);
        return level switch
        {
            LogLevel.High => MessageImportance.High,
            LogLevel.Normal => MessageImportance.Normal,
            LogLevel.Low => MessageImportance.Low,
            _ => SponsorCheckLog.DefaultImportance(BuildServerDetector.Detected)
        };
    }

    bool TryResolveLogLevels(TaskLoggingHelper log, out LogLevels levels)
    {
        if (LogLevels.TryParse(MessageLevel, WarningLevel, out levels, out var invalid))
        {
            return true;
        }

        var problems = string.Join(" ", invalid.Select(_ => $"{_.Property}='{_.Value}' is not a log level."));
        SponsorCheckLog.Error(
            log,
            "SC060",
            $"Package '{ThePackageId}': {problems} Valid levels are warning, high, normal and low; an unset property keeps the default.");
        return false;
    }

    bool Verify(TaskLoggingHelper log)
    {
        try
        {
            var context = BuildConsumerContext();

            // SC020 enforces that under CPM only <PackageVersion> carries SponsorCheck metadata,
            // and conversely under non-CPM only <PackageReference> does. Run this before merging
            // so a wrong-side value doesn't silently flow through the merge. Owner mode reads global
            // properties (single source), so placement doesn't apply.
            if (!context.IsOwner && !CheckPlacement(context, log))
            {
                return false;
            }

            var ignored = PackageMetadataMerger.Merge("SponsorshipLicenseIgnored", IgnoredFromRef, IgnoredFromVer);
            var licensedUntil = PackageMetadataMerger.Merge("SponsorshipLicensedUntil", LicensedUntilFromRef, LicensedUntilFromVer);
            var exemption = PackageMetadataMerger.Merge("SponsorshipExemption", SponsorshipExemptionFromRef, SponsorshipExemptionFromVer);
            var exemptionUntil = PackageMetadataMerger.Merge("SponsorshipExemptionUntil", SponsorshipExemptionUntilFromRef, SponsorshipExemptionUntilFromVer);
            var sponsorshipStart = PackageMetadataMerger.Merge("SponsorshipStart", SponsorshipStartFromRef, SponsorshipStartFromVer);
            var privateUntil = PackageMetadataMerger.Merge("SponsorshipPrivateUntil", SponsorshipPrivateUntilFromRef, SponsorshipPrivateUntilFromVer);
            var sponsors = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GitHubSponsors"] = PackageMetadataMerger.Merge("GitHubSponsorAccount", GitHubFromRef, GitHubFromVer),
                ["OpenCollective"] = PackageMetadataMerger.Merge("OpenCollectiveSponsorAccount", OpenCollectiveFromRef, OpenCollectiveFromVer),
                ["Polar"] = PackageMetadataMerger.Merge("PolarSponsorAccount", PolarFromRef, PolarFromVer)
            };

            var decision = LicenseModeResolver.Resolve(ignored, licensedUntil, exemption, exemptionUntil, sponsors, sponsorshipStart, ThePackageId, privateUntil);
            // These four sidecar files back only diagnostic rendering (and the exemption lookup), so
            // wrap each read in Lazy — a passing build (sponsor matches, or license valid) returns from
            // DecisionApplier without forcing any of them and reads only the pack date and hash list.
            // The landing-url read folds into the author-accounts lazy since it only shapes those URLs.
            var authorAccounts = new Lazy<IReadOnlyList<AuthorAccount>>(() => ResolveAuthorAccounts(AuthorAccountsPath, ReadLandingUrl(LandingUrlPath)));
            var exemptionsDefined = new Lazy<IReadOnlyDictionary<string, ExemptionDefinition>>(() => SponsorshipExemptionsFile.Read(ExemptionsPath));
            var severityOverrides = new Lazy<IReadOnlyDictionary<string, Severity>>(() => SeverityOverrideFile.Read(SeverityOverridesPath));
            var messageOverrides = new Lazy<IReadOnlyDictionary<string, string>>(() => MessageOverrideFile.Read(MessageOverridesPath));
            return DecisionApplier.Apply(decision, SponsorHashListPath, PackDatePath, context, authorAccounts, exemptionsDefined, severityOverrides, messageOverrides, log, DateTime.UtcNow, ResolvePrivateSponsorMaxTermMonths());
        }
        catch (MaintenanceFeeException exception)
        {
            SponsorCheckLog.Error(log, "SC019", exception.Message);
            return false;
        }
    }

    // Tolerant by design: this value comes from the publisher's packed targets, not the consumer's
    // project, so a missing or malformed one is not something the consumer can fix. Falling back to
    // the default keeps their build working; the bundler already refuses to pack a bad value (SC109).
    int ResolvePrivateSponsorMaxTermMonths()
    {
        var raw = PrivateSponsorMaxTermMonths.Trim();
        if (raw.Length > 0 &&
            int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var months) &&
            months >= 1)
        {
            return months;
        }

        return PrivateSponsorTerm.DefaultMaxTermMonths;
    }

    ConsumerContext BuildConsumerContext()
    {
        var isOwner = !string.IsNullOrWhiteSpace(OwnerId);
        var isCpm = !isOwner && string.Equals(IsCpm, "true", StringComparison.OrdinalIgnoreCase);
        var mode = isOwner
            ? ConsumerMode.Owner
            : isCpm
                ? ConsumerMode.Cpm
                : ConsumerMode.NonCpm;
        // Prefer the version from the side that's authoritative for CPM mode, but fall back to
        // either side so we still render a useful example when the consumer's setup is mixed.
        var resolvedVersion = isCpm
            ? FirstNonEmpty(PackageVersionFromVer, PackageVersionFromRef)
            : FirstNonEmpty(PackageVersionFromRef, PackageVersionFromVer);
        return new(
            mode,
            ConsumerProjectPath,
            DirectoryPackagesPropsPath,
            ThePackageId,
            resolvedVersion,
            isOwner ? OwnerId.Trim() : "");
    }

    bool CheckPlacement(ConsumerContext context, TaskLoggingHelper log)
    {
        var pairs = new (string Name, string FromRef, string FromVer)[]
        {
            ("SponsorshipLicenseIgnored", IgnoredFromRef, IgnoredFromVer),
            ("SponsorshipLicensedUntil", LicensedUntilFromRef, LicensedUntilFromVer),
            ("SponsorshipExemption", SponsorshipExemptionFromRef, SponsorshipExemptionFromVer),
            ("SponsorshipExemptionUntil", SponsorshipExemptionUntilFromRef, SponsorshipExemptionUntilFromVer),
            ("SponsorshipStart", SponsorshipStartFromRef, SponsorshipStartFromVer),
            ("SponsorshipPrivateUntil", SponsorshipPrivateUntilFromRef, SponsorshipPrivateUntilFromVer),
            ("GitHubSponsorAccount", GitHubFromRef, GitHubFromVer),
            ("OpenCollectiveSponsorAccount", OpenCollectiveFromRef, OpenCollectiveFromVer),
            ("PolarSponsorAccount", PolarFromRef, PolarFromVer)
        };

        var misplaced = new List<string>();
        foreach (var (name, fromRef, fromVer) in pairs)
        {
            var wrong = context.IsCpm ? fromRef : fromVer;
            if (!string.IsNullOrWhiteSpace(wrong))
            {
                misplaced.Add(name);
            }
        }

        if (misplaced.Count == 0)
        {
            return true;
        }

        var body = ConsumerMetadataExamples.RenderPlacementError(context, misplaced);
        SponsorCheckLog.Error(log, "SC020", body);
        return false;
    }

    static string FirstNonEmpty(string a, string b)
    {
        if (!string.IsNullOrWhiteSpace(a))
        {
            return a.Trim();
        }

        return string.IsNullOrWhiteSpace(b) ? "" : b.Trim();
    }

    public static IReadOnlyList<AuthorAccount> ResolveAuthorAccounts(string authorAccountsPath, string? landingUrlOverride = null)
    {
        var entries = AuthorAccountsFile.Read(authorAccountsPath);
        var accounts = new List<AuthorAccount>(entries.Count);
        foreach (var entry in entries)
        {
            if (PlatformRegistry.TryGet(entry.Key, out var platform))
            {
                var url = string.IsNullOrWhiteSpace(landingUrlOverride)
                    ? platform!.SponsorPageUrl(entry.Value)
                    : landingUrlOverride!.Trim();
                accounts.Add(
                    new(
                        entry.Key,
                        entry.Value,
                        url,
                        ConsumerMetadataNames.For(entry.Key)));
            }
        }

        return accounts;
    }

    static string? ReadLandingUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text;
    }
}
