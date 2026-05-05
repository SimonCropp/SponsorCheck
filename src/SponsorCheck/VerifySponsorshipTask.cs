public sealed class VerifySponsorshipTask :
    Microsoft.Build.Utilities.Task
{
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string SponsorHashListPath { get; set; } = "";
    [Required] public string PackDatePath { get; set; } = "";
    [Required] public string AuthorAccountsPath { get; set; } = "";

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
            var sponsorUrls = ResolveSponsorUrls(AuthorAccountsPath);
            return DecisionApplier.Apply(decision, SponsorHashListPath, PackDatePath, sponsorUrls, Log, DateTime.UtcNow);
        }
        catch (MaintenanceFeeException exception)
        {
            SponsorCheckLog.Error(Log, "SC006", exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    public static IReadOnlyList<string> ResolveSponsorUrls(string authorAccountsPath)
    {
        var entries = AuthorAccountsFile.Read(authorAccountsPath);
        var urls = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            if (PlatformRegistry.TryGet(entry.Key, out var platform))
            {
                urls.Add(platform!.SponsorPageUrl(entry.Value));
            }
        }

        return urls;
    }
}