namespace SponsorCheck.Tests;

public class SponsorOverrideFileTests
{
    static string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sponsorcheck-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task ReadsValidArray()
    {
        var path = WriteTempJson("""
        [
          { "platform": "GitHubSponsors", "account": "alice" },
          { "platform": "OpenCollective", "account": "acme-org" },
          { "platform": "Polar",          "account": "acme" }
        ]
        """);

        var entries = SponsorOverrideFile.Read(path);
        await Assert.That(entries.Count).IsEqualTo(3);
        await Assert.That(entries[0]).IsEqualTo(new SponsorEntry("GitHubSponsors", "alice"));
        await Assert.That(entries[1]).IsEqualTo(new SponsorEntry("OpenCollective", "acme-org"));
        await Assert.That(entries[2]).IsEqualTo(new SponsorEntry("Polar", "acme"));
    }

    [Test]
    public async Task EmptyArrayOk()
    {
        var path = WriteTempJson("[]");
        var entries = SponsorOverrideFile.Read(path);
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public void MissingFileThrows() =>
        Assert.Throws<MaintenanceFeeException>(() =>
            SponsorOverrideFile.Read(Path.Combine(Path.GetTempPath(), "does-not-exist.json")));

    [Test]
    public void NonArrayRootThrows()
    {
        var path = WriteTempJson("""{ "platform": "X", "account": "Y" }""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }

    [Test]
    public void MissingPlatformThrows()
    {
        var path = WriteTempJson("""[{ "account": "alice" }]""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }

    [Test]
    public void MissingAccountThrows()
    {
        var path = WriteTempJson("""[{ "platform": "GitHubSponsors" }]""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }
}
