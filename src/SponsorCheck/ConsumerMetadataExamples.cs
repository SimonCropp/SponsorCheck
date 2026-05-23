// Renders the multi-line "here's what to paste" blocks that wrap each consumer-side diagnostic
// message. Centralised so the choice of <PackageReference> vs <PackageVersion> and the target
// file path stay consistent across SC001/SC005/SC007/SC009/SC011/SC013/SC015/SC020.
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
        // Sponsor options come first, then time-bounded license, then the ignore escape hatch
        // last — same ordering convention as the SC003/SC004 conflict message.
        var lines = context.IsOwner
            ? new List<string>
            {
                "Set ONE of the following properties (in a <PropertyGroup> in Directory.Build.props or the consuming project):"
            }
            : new List<string>
            {
                $"Add ONE of the following attributes to the existing <{context.ElementName}> for '{context.PackageId}' in:",
                $"  {context.TargetFilePath}"
            };

        foreach (var account in authorAccounts)
        {
            lines.Add("");
            lines.Add($"Option — Sponsor on {FriendlyPlatformName(account.PlatformId)} ({account.SponsorUrl}):");
            lines.Add($"  {RenderItem(context, (account.MetadataName, $"<your-{LowerHyphen(account.PlatformId)}-account>"))}");
        }

        lines.Add("");
        lines.Add("Option — Time-bounded license (replace yyyy-MM with the last covered month):");
        lines.Add($"  {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}");

        lines.Add("");
        lines.Add("Option — Mark as ignored (you accept that the build is in breach of the package license):");
        lines.Add($"  {RenderItem(context, ("SponsorshipLicenseIgnored", "true"))}");

        var sponsorAt = RenderSponsorAtBlock(authorAccounts);
        if (sponsorAt.Length > 0)
        {
            lines.Add("");
            lines.Add(sponsorAt);
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
            : [(ConsumerMetadataNames.For(pair.Key), pair.Value)];
        var attributes = existing.Concat([("SponsorshipStart", "yyyy-MM-dd")]).ToArray();

        if (context.IsOwner)
        {
            return $"""
                    If sponsorship started after this package was released, attest to the start date by setting the SponsorshipStart property in Directory.Build.props or the consuming project.

                    Example format:

                      {RenderItem(context, attributes)}
                    """;
        }

        return $"""
                If sponsorship started after this package was released, attest to the start date in:

                  {context.TargetFilePath}

                Example format:

                  {RenderItem(context, attributes)}
                """;
    }

    // Renders the "Sponsor at..." block used in SC007-SC010 messages. Single-platform collapses
    // to an inline "Sponsor at {url}" line; multiple platforms use a "Sponsor at:\n  {url1}\n  {url2}" block.
    // Returns an empty string when there are no platforms (caller is responsible for not adding a
    // leading blank line in that case).
    public static string RenderSponsorAtBlock(IReadOnlyList<AuthorAccount> authorAccounts)
    {
        if (authorAccounts.Count == 0)
        {
            return "";
        }

        // Dedupe identical URLs so a SponsorLandingUrl override (which collapses every platform
        // to the same URL) renders as a single "Sponsor at <url>" line instead of repeating it.
        var distinctUrls = new List<string>();
        foreach (var account in authorAccounts)
        {
            if (!distinctUrls.Contains(account.SponsorUrl, StringComparer.Ordinal))
            {
                distinctUrls.Add(account.SponsorUrl);
            }
        }

        if (distinctUrls.Count == 1)
        {
            return $"Sponsor at {distinctUrls[0]}";
        }

        var lines = new List<string> { "Sponsor at:" };
        foreach (var url in distinctUrls)
        {
            lines.Add($"  {url}");
        }

        return string.Join(newline, lines);
    }

    public static string RenderLicensedUntilRenewal(ConsumerContext context) =>
        context.IsOwner
            ? $"""
               Renew the license by updating the SponsorshipLicensedUntil property in Directory.Build.props or the consuming project.

               Example format:

                 {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}
               """
            : $"""
               Renew the license in:

                 {context.TargetFilePath}

               Example format:

                 {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}
               """;

    public static string RenderLicensedUntilFormatFix(ConsumerContext context) =>
        context.IsOwner
            ? $"""
               Fix the SponsorshipLicensedUntil property in Directory.Build.props or the consuming project.

               Example format:

                 {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}
               """
            : $"""
               Fix the SponsorshipLicensedUntil attribute in:

                 {context.TargetFilePath}

               Example format:

                 {RenderItem(context, ("SponsorshipLicensedUntil", "yyyy-MM"))}
               """;

    public static string RenderSponsorshipStartFix(ConsumerContext context) =>
        context.IsOwner
            ? $"""
               Fix the SponsorshipStart property in Directory.Build.props or the consuming project.

               Example format:

                 {RenderItem(context, ("SponsorshipStart", "yyyy-MM-dd"))}
               """
            : $"""
               Fix the SponsorshipStart attribute in:

                 {context.TargetFilePath}

               Example format:

                 {RenderItem(context, ("SponsorshipStart", "yyyy-MM-dd"))}
               """;

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
            ""
        };

        // Single misplaced attribute: collapse the move-off / move-onto pair into one sentence.
        if (misplacedMetadataNames.Count == 1)
        {
            lines.Add($"Move the {misplacedMetadataNames[0]} attribute from the <{wrongElement}> for '{context.PackageId}' to the <{rightElement}> for '{context.PackageId}' in:");
            lines.Add($"  {rightFile}");
            return string.Join(newline, lines);
        }

        lines.Add($"Move the following attributes off the <{wrongElement}> for '{context.PackageId}'");
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
        // Owner mode is property-based: render each license mode as its own MSBuild property element
        // rather than item metadata on a <PackageReference>/<PackageVersion>. Continuation lines are
        // indented two spaces so multi-property examples stay aligned under the caller's leading indent.
        if (context.IsOwner)
        {
            return string.Join($"{newline}  ", extraAttributes.Select(_ => $"<{_.Attribute}>{_.Value}</{_.Attribute}>"));
        }

        // Both <PackageReference> (no-CPM) and <PackageVersion> (CPM) carry a Version attribute,
        // so always render it.
        var sb = new StringBuilder();
        sb.Append($"<{context.ElementName} Include=\"{context.PackageId}\"");
        sb.Append($" Version=\"{context.DisplayVersion}\"");
        foreach (var (attribute, value) in extraAttributes)
        {
            sb.Append($" {attribute}=\"{value}\"");
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
