namespace SponsorCheck.Web.Tests;

/// <summary>Builds in-memory nupkgs whose SponsorCheck sidecar files mirror what the real bundler
/// writes (formats pinned by RepoContractTests), for NupkgParser and lookup tests.</summary>
public static class TestNupkg
{
    public static byte[] Build(
        string packageId = "ThePackage",
        bool sponsorCheck = true,
        bool transitive = false,
        string? ownerId = null,
        string packDate = "2026-01-15",
        string landingUrl = "",
        IReadOnlyDictionary<string, string>? exemptions = null,
        IReadOnlyDictionary<string, (string Message, int? MaxTermMonths)>? boundedExemptions = null,
        IReadOnlyDictionary<string, string>? accounts = null,
        IReadOnlyDictionary<string, string>? severities = null,
        int paddingBytes = 0)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string name, string content)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            Write($"lib/netstandard2.0/{packageId}.dll", "stub assembly bytes");

            if (sponsorCheck)
            {
                var folder = transitive ? "buildTransitive/" : "build/";
                var enabledAccounts = accounts ?? new Dictionary<string, string> { ["GitHubSponsors"] = "acmecorp" };

                Write(folder + "SponsorCheck.SponsorHashes.txt", "001122334455\n66778899aabb\n");
                Write(folder + "SponsorCheck.PackDate.txt", packDate);
                Write(folder + "SponsorCheck.LandingUrl.txt", landingUrl);
                Write(folder + "SponsorCheck.AuthorAccounts.txt",
                    string.Join('\n', enabledAccounts.Select(_ => $"{_.Key}={_.Value}")));
                Write(folder + "SponsorCheck.SeverityOverrides.txt",
                    string.Join('\n', (severities ?? new Dictionary<string, string>()).Select(_ => $"{_.Key}={_.Value}")));
                // `exemptions` writes the bare-string shape packages published before MaxTermMonths
                // carry; `boundedExemptions` writes the object shape the current bundler emits.
                // Both are live on nuget.org, so both need coverage in the parser.
                var exemptionEntries = boundedExemptions != null
                    ? boundedExemptions.Select(_ => _.Value.MaxTermMonths is { } months
                        ? $"\"{_.Key}\": {{ \"message\": \"{_.Value.Message}\", \"maxTermMonths\": {months} }}"
                        : $"\"{_.Key}\": {{ \"message\": \"{_.Value.Message}\" }}")
                    : (exemptions ?? new Dictionary<string, string>()).Select(_ => $"\"{_.Key}\": \"{_.Value}\"");
                Write(folder + "SponsorCheck.Exemptions.json", "{" + string.Join(',', exemptionEntries) + "}");

                var ownerProperty = ownerId == null
                    ? ""
                    : $"\n    <_SponsorCheck_OwnerId>{ownerId}</_SponsorCheck_OwnerId>";
                Write(folder + $"{packageId}.targets",
                    $"<Project>\n  <PropertyGroup>\n    <_SponsorCheck_ThePackageId>{packageId}</_SponsorCheck_ThePackageId>{ownerProperty}\n  </PropertyGroup>\n</Project>");
            }

            // Simulates a large package: stored (uncompressed) filler written *after* the
            // sidecars, so they sit deep in the file rather than at its end. Padding first
            // would leave them inside the tail RemoteZip already downloaded when opening,
            // where they are served for free and the ranged read path goes untested.
            if (paddingBytes > 0)
            {
                var padding = archive.CreateEntry("lib/net10.0/padding.bin", CompressionLevel.NoCompression);
                using var stream = padding.Open();
                stream.Write(new byte[paddingBytes]);
            }
        }

        return memory.ToArray();
    }
}
