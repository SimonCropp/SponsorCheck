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
}
