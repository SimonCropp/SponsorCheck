namespace EnforceOssSponsorship.Tests;

public class SponsorHasherTests
{
    [Test]
    public async Task Deterministic()
    {
        var first = SponsorHasher.Hash("GitHubSponsors", "alice");
        var second = SponsorHasher.Hash("GitHubSponsors", "alice");
        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task LowercasesAccount()
    {
        var lower = SponsorHasher.Hash("GitHubSponsors", "alice");
        var mixed = SponsorHasher.Hash("GitHubSponsors", "Alice");
        var upper = SponsorHasher.Hash("GitHubSponsors", "ALICE");
        await Assert.That(mixed).IsEqualTo(lower);
        await Assert.That(upper).IsEqualTo(lower);
    }

    [Test]
    public async Task TrimsAccount()
    {
        var trimmed = SponsorHasher.Hash("GitHubSponsors", "alice");
        var padded = SponsorHasher.Hash("GitHubSponsors", "  alice  ");
        await Assert.That(padded).IsEqualTo(trimmed);
    }

    [Test]
    public async Task PlatformPrefixDistinguishes()
    {
        var gh = SponsorHasher.Hash("GitHubSponsors", "alice");
        var oc = SponsorHasher.Hash("OpenCollective", "alice");
        var po = SponsorHasher.Hash("Polar", "alice");
        await Assert.That(gh).IsNotEqualTo(oc);
        await Assert.That(gh).IsNotEqualTo(po);
        await Assert.That(oc).IsNotEqualTo(po);
    }

    [Test]
    public async Task IsLowercaseHex64Chars()
    {
        var hash = SponsorHasher.Hash("GitHubSponsors", "alice");
        await Assert.That(hash.Length).IsEqualTo(64);
        await Assert.That(hash).Matches("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task KnownVector()
    {
        // Locked baseline; if this changes, every previously bundled package becomes invalid.
        var hash = SponsorHasher.Hash("GitHubSponsors", "alice");
        await Verify(hash);
    }

    [Test]
    public void RejectsEmptyPlatform() =>
        Assert.Throws<ArgumentException>(() => SponsorHasher.Hash("", "alice"));

    [Test]
    public void RejectsEmptyAccount() =>
        Assert.Throws<ArgumentException>(() => SponsorHasher.Hash("GitHubSponsors", ""));
}
