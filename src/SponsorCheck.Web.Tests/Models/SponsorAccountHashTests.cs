namespace SponsorCheck.Web.Tests.Models;

public class SponsorAccountHashTests
{
    [Test]
    // The same locked baseline SponsorHasherTests pins in the task assembly. Sharing the vector rather
    // than picking a fresh one is the point: it is the value already bundled into every published
    // package, so if either implementation ever drifts, both tests fail and say so.
    public async Task KnownVector() =>
        await Assert.That(SponsorAccountHash.For("GitHubSponsors", "alice")).IsEqualTo("559b6b66f4b3");

    [Test]
    [Arguments("alice")]
    [Arguments("Alice")]
    [Arguments("ALICE")]
    [Arguments("  alice  ")]
    public async Task AccountsAreTrimmedAndLowercased(string account) =>
        // A consumer types their account however they please; the bundler hashed it folded, so the
        // wizard has to fold it the same way or a real sponsor is told they are not in the list.
        await Assert.That(SponsorAccountHash.For("GitHubSponsors", account)).IsEqualTo("559b6b66f4b3");

    [Test]
    public async Task ThePlatformIdIsPartOfTheHash()
    {
        // Same account on two platforms is two different sponsorships, so the ids must not collide —
        // and the id is NOT case-folded (only the account is), matching the bundler.
        var github = SponsorAccountHash.For("GitHubSponsors", "alice");
        await Assert.That(SponsorAccountHash.For("OpenCollective", "alice")).IsNotEqualTo(github);
        await Assert.That(SponsorAccountHash.For("githubsponsors", "alice")).IsNotEqualTo(github);
    }

    [Test]
    public async Task EveryPlatformsWireIdHashesToTwelveLowercaseHexChars()
    {
        // The wire ids are what the bundler keys on, so hashing through Platform.WireId is what makes
        // the wizard's lookup line up with the file rather than with its own naming.
        foreach (var platform in Platform.All)
        {
            var hash = SponsorAccountHash.For(platform.WireId, "alice");
            await Assert.That(hash).Matches("^[0-9a-f]{12}$");
        }
    }
}
