namespace SponsorCheck.Web.Services;

/// <summary>
/// Pure extraction of <see cref="PackageFacts"/> from a nupkg exposed as a
/// <see cref="RemoteZipArchive"/>. The file names and formats mirror what
/// BundleSponsorListTask writes; RepoContractTests pin them against the shipped targets
/// templates so this parser can't silently rot. Only the tiny sidecar files and the
/// verifier targets are ever downloaded — SponsorHashes.txt is existence-checked from the
/// central directory alone — so cost is independent of package and sponsor-list size.
/// </summary>
public static class NupkgParser
{
    public const string HashesFileName = "SponsorCheck.SponsorHashes.txt";
    public const string PackDateFileName = "SponsorCheck.PackDate.txt";
    public const string AuthorAccountsFileName = "SponsorCheck.AuthorAccounts.txt";
    public const string SeverityOverridesFileName = "SponsorCheck.SeverityOverrides.txt";
    public const string LandingUrlFileName = "SponsorCheck.LandingUrl.txt";
    public const string ExemptionsFileName = "SponsorCheck.Exemptions.json";
    /// <summary>
    /// Matches the owner id element in a generated owner-mode verifier. The element name is scoped
    /// to the package it ships in (see ConsumerVerifierOwner.targets for why), so the middle segment
    /// varies per package and is captured rather than pinned. The optional segment also keeps
    /// packages published before scoping — whose element is the bare `_SponsorCheck_OwnerId` —
    /// readable, since this parser runs against whatever is already on nuget.org, not just against
    /// packages built from current source. The backreference stops a mismatched open/close pair
    /// from parsing.
    /// </summary>
    public const string OwnerIdElementPattern = @"<(_SponsorCheck_(?:\w+_)?OwnerId)>(.+?)</\1>";

    /// <summary>
    /// Matches the private-sponsorship cap element in a generated verifier. Same package-scoped
    /// element-name shape as <see cref="OwnerIdElementPattern"/>, and absent from packages published
    /// before the cap existed — those fall back to
    /// <see cref="PackageFacts.DefaultPrivateSponsorMaxTermMonths"/>, which is what their verifier
    /// does too.
    /// </summary>
    public const string PrivateSponsorMaxTermMonthsElementPattern = @"<(_SponsorCheck_(?:\w+_)?PrivateSponsorMaxTermMonths)>(.+?)</\1>";

    public static async Task<PackageFacts> Parse(string packageId, string version, RemoteZipArchive nupkg)
    {
        // buildTransitive/ is used when the author enabled CheckTransitiveReferences; build/ otherwise.
        var folder = "buildTransitive/";
        var checkTransitive = true;
        if (nupkg.Find(folder + HashesFileName) == null)
        {
            folder = "build/";
            checkTransitive = false;
            if (nupkg.Find(folder + HashesFileName) == null)
            {
                return PackageFacts.WithoutSponsorCheck(packageId, version);
            }
        }

        var sidecars = new[]
        {
            PackDateFileName,
            LandingUrlFileName,
            AuthorAccountsFileName,
            ExemptionsFileName,
            SeverityOverridesFileName
        };
        var targets = nupkg.Entries
            .Where(_ => _.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                        _.FullName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) &&
                        _.FullName.LastIndexOf('/') < folder.Length)
            .Select(_ => _.FullName)
            .ToList();

        // One batched read: the bundler packs these adjacently, so against a range-capable
        // server this coalesces into a single request. Names absent from the archive are
        // absent from the result, so no existence checks are needed for optional sidecars.
        var contents = await nupkg.ReadText([.. sidecars.Select(_ => folder + _), .. targets]);

        string? Text(string name) => contents.GetValueOrDefault(folder + name);

        var packDate = NullIfBlank(Text(PackDateFileName));
        var landingUrl = NullIfBlank(Text(LandingUrlFileName));
        var platforms = ParseAuthorAccounts(Text(AuthorAccountsFileName));
        var exemptions = ParseExemptions(Text(ExemptionsFileName));
        var severities = ParseSeverities(Text(SeverityOverridesFileName));
        var ownerId = FindOwnerId(targets, contents);
        var privateSponsorMaxTermMonths = FindPrivateSponsorMaxTermMonths(targets, contents);

        return new(
            packageId,
            version,
            BundlesSponsorCheck: true,
            CheckTransitive: checkTransitive,
            OwnerMode: ownerId != null,
            OwnerId: ownerId,
            PackDate: packDate,
            LandingUrl: landingUrl,
            Platforms: platforms,
            Exemptions: exemptions,
            Severities: severities,
            PrivateSponsorMaxTermMonths: privateSponsorMaxTermMonths);
    }

    static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    static List<PackagePlatformAccount> ParseAuthorAccounts(string? content)
    {
        var result = new List<PackagePlatformAccount>();
        if (content == null)
        {
            return result;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var platform = Platform.FromWireId(line[..separator].Trim());
            var account = line[(separator + 1)..].Trim();
            if (platform != null && account.Length > 0)
            {
                result.Add(new(platform.Kind, account));
            }
        }

        return result;
    }

    static List<PackageExemption> ParseExemptions(string? content)
    {
        var result = new List<PackageExemption>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return result;
        }

        try
        {
            // JsonDocument rather than JsonSerializer: reflection-free, so safe under full trimming.
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Trim().Length == 0)
                {
                    continue;
                }

                // Two shapes: the object form the current bundler writes, and a bare message string,
                // which is what packages published before MaxTermMonths existed carry. Both are live
                // on nuget.org, so both have to read.
                var value = property.Value;
                if (value.ValueKind == JsonValueKind.String)
                {
                    var bare = value.GetString();
                    if (!string.IsNullOrWhiteSpace(bare))
                    {
                        result.Add(new(property.Name, bare));
                    }

                    continue;
                }

                if (value.ValueKind != JsonValueKind.Object ||
                    !value.TryGetProperty("message", out var messageElement) ||
                    messageElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var message = messageElement.GetString();
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                int? maxTermMonths = null;
                if (value.TryGetProperty("maxTermMonths", out var monthsElement) &&
                    monthsElement.ValueKind == JsonValueKind.Number &&
                    monthsElement.TryGetInt32(out var months) &&
                    months > 0)
                {
                    maxTermMonths = months;
                }

                result.Add(new(property.Name, message, maxTermMonths));
            }
        }
        catch (JsonException)
        {
            // Tolerate a malformed sidecar the same way the verifier does: no exemptions defined.
        }

        return result;
    }

    static Dictionary<string, string> ParseSeverities(string? content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (content == null)
        {
            return result;
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            result[line[..separator].Trim()] = line[(separator + 1)..].Trim().ToLowerInvariant();
        }

        return result;
    }

    static int FindPrivateSponsorMaxTermMonths(List<string> targets, IReadOnlyDictionary<string, string> contents)
    {
        foreach (var name in targets)
        {
            if (!contents.TryGetValue(name, out var content))
            {
                continue;
            }

            var match = Regex.Match(content, PrivateSponsorMaxTermMonthsElementPattern);
            if (match.Success &&
                int.TryParse(match.Groups[2].Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var months) &&
                months >= 1)
            {
                return months;
            }
        }

        return PackageFacts.DefaultPrivateSponsorMaxTermMonths;
    }

    static string? FindOwnerId(List<string> targets, IReadOnlyDictionary<string, string> contents)
    {
        foreach (var name in targets)
        {
            if (!contents.TryGetValue(name, out var content) ||
                !content.Contains("_SponsorCheck_"))
            {
                continue;
            }

            var match = Regex.Match(content, OwnerIdElementPattern);
            if (match.Success)
            {
                return match.Groups[2].Value.Trim();
            }

            // The per-package verifier targets have no owner id element.
            return null;
        }

        return null;
    }
}
