namespace SponsorCheck.Web.Services;

/// <summary>
/// Pure extraction of <see cref="PackageFacts"/> from a nupkg stream. The file names and formats
/// mirror what BundleSponsorListTask writes; RepoContractTests pin them against the shipped
/// targets templates so this parser can't silently rot.
/// </summary>
public static class NupkgParser
{
    public const string HashesFileName = "SponsorCheck.SponsorHashes.txt";
    public const string PackDateFileName = "SponsorCheck.PackDate.txt";
    public const string AuthorAccountsFileName = "SponsorCheck.AuthorAccounts.txt";
    public const string SeverityOverridesFileName = "SponsorCheck.SeverityOverrides.txt";
    public const string LandingUrlFileName = "SponsorCheck.LandingUrl.txt";
    public const string ExemptionsFileName = "SponsorCheck.Exemptions.json";
    public const string OwnerIdElement = "_SponsorCheck_OwnerId";

    public static PackageFacts Parse(string packageId, string version, Stream nupkg)
    {
        using var archive = new ZipArchive(nupkg, ZipArchiveMode.Read, leaveOpen: true);

        // buildTransitive/ is used when the author enabled CheckTransitiveReferences; build/ otherwise.
        var folder = "buildTransitive/";
        var checkTransitive = true;
        if (archive.GetEntry(folder + HashesFileName) == null)
        {
            folder = "build/";
            checkTransitive = false;
            if (archive.GetEntry(folder + HashesFileName) == null)
            {
                return PackageFacts.WithoutSponsorCheck(packageId, version);
            }
        }

        string? ReadEntry(string name)
        {
            var entry = archive.GetEntry(folder + name);
            if (entry == null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        var packDate = NullIfBlank(ReadEntry(PackDateFileName));
        var landingUrl = NullIfBlank(ReadEntry(LandingUrlFileName));
        var platforms = ParseAuthorAccounts(ReadEntry(AuthorAccountsFileName));
        var exemptions = ParseExemptions(ReadEntry(ExemptionsFileName));
        var severities = ParseSeverities(ReadEntry(SeverityOverridesFileName));
        var ownerId = FindOwnerId(archive, folder);

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
            Severities: severities);
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
                var value = property.Value;
                var message = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (property.Name.Trim().Length > 0 &&
                    !string.IsNullOrWhiteSpace(message))
                {
                    result.Add(new(property.Name, message));
                }
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

    static string? FindOwnerId(ZipArchive archive, string folder)
    {
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
                !entry.FullName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.LastIndexOf('/') >= folder.Length)
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            if (!content.Contains("_SponsorCheck_"))
            {
                continue;
            }

            var match = Regex.Match(content, $"<{OwnerIdElement}>(.+?)</{OwnerIdElement}>");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // The per-package verifier targets have no owner id element.
            return null;
        }

        return null;
    }
}
