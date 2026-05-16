namespace SponsorCheck.IntegrationTests;

using System.IO.Compression;

public class AuthorPackTests
{
    [Test]
    public async Task ProducedNupkgContainsBuildAndTasksFolders()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackage.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/SponsorCheck.dll"))).IsTrue();
        await Assert.That(entries.Any(e => e.StartsWith("tasks/net472/SponsorCheck.dll"))).IsTrue();
    }

    [Test]
    public async Task BundledHashesMatchOverrideListAndAreDeterministic()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SponsorHashes.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        // Override list has 4 entries: alice, bob (GitHub), acme-org (OC), acme (Polar)
        await Assert.That(lines.Length).IsEqualTo(4);
        for (var i = 1; i < lines.Length; i++)
        {
            await Assert.That(string.CompareOrdinal(lines[i - 1], lines[i])).IsLessThan(0).Because("output should be sorted ordinal");
        }
    }

    [Test]
    public async Task MultiTargeted_ProducedNupkgContainsBundlerOutputs()
    {
        // Multi-targeted authors hit a different MSBuild import path: NuGet's per-TFM <project>.nuget.g.targets
        // ImportGroups don't fire in the outer multi-target build (where Pack runs). buildMultiTargeting/SponsorCheck.targets
        // is what makes the bundler visible to that outer build.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageMulti");
        var nupkg = Directory.GetFiles(feed, "ThePackageMulti.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackageMulti.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/SponsorCheck.dll"))).IsTrue();
    }

    [Test]
    public async Task CpmMultiTargeted_MetadataOnPackageVersion_BundlesSuccessfully()
    {
        // Regression: when SponsorCheck metadata lives on <PackageVersion> (CPM) and the author project
        // multi-targets, MSBuild *task batches* on `%(ItemGroup.Metadata)` parameters when two ItemGroups
        // are in scope (PackageReference batch + PackageVersion batch). The PackageReference batch under
        // CPM has no SponsorCheck metadata, so that invocation tripped SC101 even though the PackageVersion
        // batch succeeded. Fix: SponsorCheck.targets flattens metadata into scalar properties first
        // (`@(Items->'%(M)')`), which kills the batching. Same fix in ConsumerVerifier.targets — see
        // ConsumerBuildTests.CpmConsumer_LicenseMetadataOnPackageVersion_PassesWithoutBatchingError.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageCpm");
        var nupkg = Directory.GetFiles(feed, "ThePackageCpm.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackageCpm.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
    }

    [Test]
    public async Task BundledTargetsReferencesRightAssembly()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/ThePackage.targets")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("VerifySponsorshipTask");
        await Assert.That(content).Contains("_SponsorCheck_Verify_ThePackage");
    }

    [Test]
    public async Task SeverityOverrides_BundledIntoNupkg()
    {
        // ThePackageOverridden declares NoLicenseSpecifiedSeverityOverride="warning" and
        // LicenseIgnoredSeverityOverride="error" on its SponsorCheck reference. The bundler
        // should write the resolved pairs to build/SponsorCheck.SeverityOverrides.txt — that's
        // what the verifier reads at consumer build time.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOverridden");
        var nupkg = Directory.GetFiles(feed, "ThePackageOverridden.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SeverityOverrides.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        // Each override metadatum applies to both the SC0xx code and its SC2xx CPM sibling, so the
        // bundled file carries two entries per author-supplied override.
        string[] expected = ["SC001=warning", "SC002=warning", "SC005=error", "SC006=error"];
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines).IsEquivalentTo(expected);
    }

    [Test]
    public async Task MessageOverrides_BundledIntoNupkg()
    {
        // ThePackageOverridden also declares NoLicenseSpecified/LicenseIgnoredMessageOverride —
        // the JSON sidecar must contain the verbatim author-supplied strings.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOverridden");
        var nupkg = Directory.GetFiles(feed, "ThePackageOverridden.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.MessageOverrides.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("Please sponsor ThePackageOverridden before using.");
        await Assert.That(content).Contains("You agreed not to free-ride this library.");
    }

    [Test]
    public async Task SeverityOverrides_AbsentOnUnoverriddenPackage()
    {
        // The bundler always writes the sidecar (for determinism/path resolution) but it's empty
        // when no override metadata is declared.
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SeverityOverrides.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content.Trim()).IsEqualTo("");
    }

    [Test]
    public async Task MessageOverrides_EmptyJsonOnUnoverriddenPackage()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.MessageOverrides.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task NoPlatformAccount_FailsPackWithSC101()
    {
        // ThePackageNoPlatform references SponsorCheck without any <Platform>Account metadata.
        // The bundler must fail-fast with SC101 before any platform fetch is attempted.
        var result = await ThePackageBuilder.TryPack("ThePackageNoPlatform");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC101");
        await Assert.That(result.Combined).Contains("at least one platform account metadata");
    }

    [Test]
    public async Task MissingCredential_FailsPackWithSC102()
    {
        // ThePackageMissingCredential declares GitHubSponsorsAccount but supplies no token.
        // Pack without the override list so the real platform fetch is attempted, and force
        // GitHubToken="" so any ambient env-var doesn't satisfy the credential requirement.
        var result = await ThePackageBuilder.TryPack(
            "ThePackageMissingCredential",
            useOverrideList: false,
            extraProperties: new Dictionary<string, string>
            {
                ["GitHubToken"] = ""
            });
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC102");
        await Assert.That(result.Combined).Contains("GitHub Sponsors: API token required");
    }

    [Test]
    public async Task SeverityOverrides_InvalidValue_FailsPackWithSC104()
    {
        // ThePackageBadOverride declares NoLicenseSpecifiedSeverityOverride="critical" — not a
        // recognized severity. Pack should fail with SC104, naming the offending metadata.
        var result = await ThePackageBuilder.TryPack("ThePackageBadOverride");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC104");
        await Assert.That(result.Combined).Contains("NoLicenseSpecifiedSeverityOverride");
        await Assert.That(result.Combined).Contains("critical");
    }
}
