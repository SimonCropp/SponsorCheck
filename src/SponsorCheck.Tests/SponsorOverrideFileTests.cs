public class SponsorOverrideFileTests
{
    static string WriteTempJson(TempDirectory dir, string content)
    {
        var path = Path.Combine(dir, "override.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task ReadsValidArray()
    {
        using var dir = new TempDirectory();
        var path = WriteTempJson(dir, """
        [
          { "platform": "GitHubSponsors", "account": "alice" },
          { "platform": "OpenCollective", "account": "acme-org" },
          { "platform": "Polar",          "account": "acme" }
        ]
        """);

        var entries = SponsorOverrideFile.Read(path);
        await Assert.That(entries.Count).IsEqualTo(3);
        await Assert.That(entries[0]).IsEqualTo(new("GitHubSponsors", "alice"));
        await Assert.That(entries[1]).IsEqualTo(new("OpenCollective", "acme-org"));
        await Assert.That(entries[2]).IsEqualTo(new("Polar", "acme"));
    }

    [Test]
    public async Task EmptyArrayOk()
    {
        using var dir = new TempDirectory();
        var path = WriteTempJson(dir, "[]");
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
        using var dir = new TempDirectory();
        var path = WriteTempJson(dir, """{ "platform": "X", "account": "Y" }""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }

    [Test]
    public void MissingPlatformThrows()
    {
        using var dir = new TempDirectory();
        var path = WriteTempJson(dir, """[{ "account": "alice" }]""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }

    [Test]
    public void MissingAccountThrows()
    {
        using var dir = new TempDirectory();
        var path = WriteTempJson(dir, """[{ "platform": "GitHubSponsors" }]""");
        Assert.Throws<MaintenanceFeeException>(() => SponsorOverrideFile.Read(path));
    }
}
