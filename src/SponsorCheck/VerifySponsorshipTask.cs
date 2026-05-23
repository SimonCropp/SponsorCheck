public sealed class VerifySponsorshipTask :
    Microsoft.Build.Utilities.Task
{
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string SponsorHashListPath { get; set; } = "";
    [Required] public string PackDatePath { get; set; } = "";
    [Required] public string AuthorAccountsPath { get; set; } = "";
    public string SeverityOverridesPath { get; set; } = "";
    public string MessageOverridesPath { get; set; } = "";
    public string LandingUrlPath { get; set; } = "";

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
    public string SponsorshipStartFromRef { get; set; } = "";
    public string SponsorshipStartFromVer { get; set; } = "";
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
            var context = BuildConsumerContext();

            // SC020 enforces that under CPM only <PackageVersion> carries SponsorCheck metadata,
            // and conversely under non-CPM only <PackageReference> does. Run this before merging
            // so a wrong-side value doesn't silently flow through the merge. Owner mode reads global
            // properties (single source), so placement doesn't apply.
            if (!context.IsOwner && !CheckPlacement(context))
            {
                return false;
            }

            var ignored = PackageMetadataMerger.Merge("SponsorshipLicenseIgnored", IgnoredFromRef, IgnoredFromVer);
            var licensedUntil = PackageMetadataMerger.Merge("SponsorshipLicensedUntil", LicensedUntilFromRef, LicensedUntilFromVer);
            var sponsorshipStart = PackageMetadataMerger.Merge("SponsorshipStart", SponsorshipStartFromRef, SponsorshipStartFromVer);
            var sponsors = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GitHubSponsors"] = PackageMetadataMerger.Merge("GitHubSponsorAccount", GitHubFromRef, GitHubFromVer),
                ["OpenCollective"] = PackageMetadataMerger.Merge("OpenCollectiveSponsorAccount", OpenCollectiveFromRef, OpenCollectiveFromVer),
                ["Polar"] = PackageMetadataMerger.Merge("PolarSponsorAccount", PolarFromRef, PolarFromVer)
            };

            var decision = LicenseModeResolver.Resolve(ignored, licensedUntil, sponsors, sponsorshipStart, ThePackageId);
            var landingUrl = ReadLandingUrl(LandingUrlPath);
            var authorAccounts = ResolveAuthorAccounts(AuthorAccountsPath, landingUrl);
            var severityOverrides = SeverityOverrideFile.Read(SeverityOverridesPath);
            var messageOverrides = MessageOverrideFile.Read(MessageOverridesPath);
            return DecisionApplier.Apply(decision, SponsorHashListPath, PackDatePath, context, authorAccounts, severityOverrides, messageOverrides, Log, DateTime.UtcNow);
        }
        catch (MaintenanceFeeException exception)
        {
            SponsorCheckLog.Error(Log, "SC019", exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
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

    bool CheckPlacement(ConsumerContext context)
    {
        var pairs = new (string Name, string FromRef, string FromVer)[]
        {
            ("SponsorshipLicenseIgnored", IgnoredFromRef, IgnoredFromVer),
            ("SponsorshipLicensedUntil", LicensedUntilFromRef, LicensedUntilFromVer),
            ("SponsorshipStart", SponsorshipStartFromRef, SponsorshipStartFromVer),
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
        SponsorCheckLog.Error(Log, "SC020", body);
        return false;
    }

    static string FirstNonEmpty(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : string.IsNullOrWhiteSpace(b) ? "" : b.Trim();

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
                accounts.Add(new(
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
        return text.Length == 0 ? null : text;
    }
}
