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
    public async Task IsLowercaseHex12Chars()
    {
        var hash = SponsorHasher.Hash("GitHubSponsors", "alice");
        await Assert.That(hash.Length).IsEqualTo(12);
        await Assert.That(hash).Matches("^[0-9a-f]{12}$");
    }

    [Test]
    public async Task KnownVector()
    {
        // Locked baseline; if this changes, every previously bundled package becomes invalid.
        var hash = SponsorHasher.Hash("GitHubSponsors", "alice");
        await Verify(hash)
            .Snapshot("559b6b66f4b3");
    }

    [Test]
    public async Task HashAll_MatchesPerEntryHash()
    {
        // The batch reuses a single SHA256 across all entries; ComputeHash re-initializes between
        // calls, so each result must equal the independent per-entry Hash. Covers a mixed-case
        // account (lowercasing) and a duplicate (identical input mid-batch → identical hash).
        var entries = new List<SponsorEntry>
        {
            new("GitHubSponsors", "alice"),
            new("GitHubSponsors", "Bob"),
            new("OpenCollective", "acme-org"),
            new("Polar", "acme"),
            new("GitHubSponsors", "alice")
        };

        var batch = SponsorHasher.HashAll(entries);
        var perEntry = entries.Select(_ => SponsorHasher.Hash(_.Platform, _.Account)).ToList();

        await Assert.That(batch.Count).IsEqualTo(perEntry.Count);
        for (var i = 0; i < perEntry.Count; i++)
        {
            await Assert.That(batch[i]).IsEqualTo(perEntry[i]);
        }
    }

    [Test]
    public void HashAll_RejectsEmptyAccountInBatch() =>
        Assert.Throws<ArgumentException>(() =>
            SponsorHasher.HashAll([new("GitHubSponsors", "alice"), new("Polar", "  ")]));

    [Test]
    public void RejectsEmptyPlatform() =>
        Assert.Throws<ArgumentException>(() => SponsorHasher.Hash("", "alice"));

    [Test]
    public void RejectsEmptyAccount() =>
        Assert.Throws<ArgumentException>(() => SponsorHasher.Hash("GitHubSponsors", ""));
}
