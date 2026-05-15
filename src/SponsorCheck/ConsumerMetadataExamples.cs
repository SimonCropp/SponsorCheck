// Renders the multi-line "here's what to paste" blocks that wrap each consumer-side diagnostic
// message. Centralised so the choice of <PackageReference> vs <PackageVersion> and the target
// file path stay consistent across SC001/SC003/SC004/SC005/SC007/SC010/SC011/SC012.
//
// All blocks use \n joins. The MSBuild console logger preserves embedded newlines on its own
// line; multi-line errors are common (e.g. C# compiler output) so this is well within MSBuild's
// normal display behaviour.
public static class ConsumerMetadataExamples
{
    const string newline = "\n";

    public static string RenderLicenseModeOptions(
        ConsumerContext context,
        IReadOnlyList<AuthorAccount> authorAccounts)
    {
        var lines = new List<string>
        {
            $"Add ONE of the following attributes to the existing <{context.ElementName}> for '{context.PackageId}' in:",
            $"  {context.TargetFilePath}",
            "",
            "Option — Mark as ignored (you accept that the build is in breach of the package license):",
            $"  {RenderItem(context, ("SponsorshipLicenseIgnored", "true"))}"
        };

        var optionNumber = 0;
        foreach (var account in authorAccounts)
        {
            optionNumber++;
            lines.Add("");
            lines.Add($"Option — Sponsor on {FriendlyPlatformName(account.PlatformId)} ({account.SponsorUrl}):");
            lines.Add($"  {RenderItem(context, (account.MetadataName, $"<your-{LowerHyphen(account.PlatformId)}-account>"))}");
        }

        lines.Add("");
        lines.Add("Option — Time-bounded license (replace yyyy-MM with the last covered month):");
        lines.Add($"  {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}");

        if (authorAccounts.Count > 0)
        {
            lines.Add("");
            lines.Add("Sponsor at:");
            foreach (var account in authorAccounts)
            {
                lines.Add($"  {account.SponsorUrl}");
            }
        }

        return string.Join(newline, lines);
    }

    public static string RenderSponsorshipStartHint(
        ConsumerContext context,
        IReadOnlyDictionary<string, string> attemptedAccounts)
    {
        // Pick whichever platform/account the consumer already supplied so the rendered example
        // shows the existing line plus the new SponsorshipStart attribute.
        var pair = attemptedAccounts.FirstOrDefault();
        var existing = pair.Key is null
            ? Array.Empty<(string, string)>()
            : new[] { (ConsumerMetadataNames.For(pair.Key), pair.Value) };
        var attributes = existing.Concat(new[] { ("SponsorshipStart", "yyyy-MM-dd") }).ToArray();

        return string.Join(newline,
        [
            "If sponsorship started after this package was released, attest to the start date in:",
            $"  {context.TargetFilePath}",
            "",
            $"  {RenderItem(context, attributes)}"
        ]);
    }

    public static string RenderLicensedUntilRenewal(ConsumerContext context) =>
        string.Join(newline,
        [
            "Renew the license in:",
            $"  {context.TargetFilePath}",
            "",
            $"  {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}"
        ]);

    public static string RenderSponsorshipStartFix(ConsumerContext context) =>
        string.Join(newline,
        [
            "Fix the SponsorshipStart attribute in:",
            $"  {context.TargetFilePath}",
            "",
            $"  {RenderItem(context, ("SponsorshipStart", "yyyy-MM-dd"))}"
        ]);

    public static string RenderPlacementError(
        ConsumerContext context,
        IReadOnlyList<string> misplacedMetadataNames)
    {
        var wrongElement = context.IsCpm ? "PackageReference" : "PackageVersion";
        var rightElement = context.ElementName;
        var rightFile = context.TargetFilePath;
        var wrongFile = context.IsCpm ? context.ConsumerProjectPath : context.DirectoryPackagesPropsPath;

        var lines = new List<string>
        {
            context.IsCpm
                ? $"Package '{context.PackageId}' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>."
                : $"Package '{context.PackageId}' is not using Central Package Management, so SponsorCheck metadata must live on <PackageReference> in the consumer csproj — not on <PackageVersion>.",
            "",
            $"Move the following attribute(s) off the <{wrongElement}> for '{context.PackageId}'"
        };

        if (!string.IsNullOrWhiteSpace(wrongFile))
        {
            lines.Add($"  in: {wrongFile}");
        }

        foreach (var name in misplacedMetadataNames)
        {
            lines.Add($"  - {name}");
        }

        lines.Add("");
        lines.Add($"...and onto the <{rightElement}> for '{context.PackageId}' in:");
        lines.Add($"  {rightFile}");

        return string.Join(newline, lines);
    }

    static string RenderItem(ConsumerContext context, params (string Attribute, string Value)[] extraAttributes)
    {
        // Both <PackageReference> (no-CPM) and <PackageVersion> (CPM) carry a Version attribute,
        // so always render it.
        var sb = new StringBuilder();
        sb.Append('<').Append(context.ElementName).Append(" Include=\"").Append(context.PackageId).Append('"');
        sb.Append(" Version=\"").Append(context.DisplayVersion).Append('"');
        foreach (var (attribute, value) in extraAttributes)
        {
            sb.Append(' ').Append(attribute).Append("=\"").Append(value).Append('"');
        }

        sb.Append(" />");
        return sb.ToString();
    }

    static string FriendlyPlatformName(string platformId) => platformId switch
    {
        "GitHubSponsors" => "GitHub Sponsors",
        "OpenCollective" => "Open Collective",
        "Polar" => "Polar",
        _ => platformId
    };

    static string LowerHyphen(string platformId) => platformId switch
    {
        "GitHubSponsors" => "github",
        "OpenCollective" => "opencollective",
        "Polar" => "polar",
        _ => platformId.ToLowerInvariant()
    };
}
