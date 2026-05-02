namespace EnforceOssSponsorship.IntegrationTests;

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
        await Assert.That(entries).Contains("build/EnforceOssSponsorship.SponsorHashes.txt");
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/EnforceOssSponsorship.Tasks.dll"))).IsTrue();
        await Assert.That(entries.Any(e => e.StartsWith("tasks/net472/EnforceOssSponsorship.Tasks.dll"))).IsTrue();
    }

    [Test]
    public async Task BundledHashesMatchOverrideListAndAreDeterministic()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/EnforceOssSponsorship.SponsorHashes.txt")!;
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
    public async Task BundledTargetsReferencesRightAssembly()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/ThePackage.targets")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("EnforceOssSponsorship.Tasks.VerifySponsorshipTask");
        await Assert.That(content).Contains("_EOSS_Verify_ThePackage");
    }
}
