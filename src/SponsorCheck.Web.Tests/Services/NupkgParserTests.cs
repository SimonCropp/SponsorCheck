namespace SponsorCheck.Web.Tests.Services;

public class NupkgParserTests
{
    /// <summary>
    /// Parses via ranged reads against nuget.org-faithful behavior, with Content-Range
    /// hidden the way browser CORS hides it. These tests cover parsing; the size and
    /// fallback behavior of the transport is covered by <see cref="PackageLookupTests"/>.
    /// </summary>
    static async Task<PackageFacts> Parse(byte[] nupkg)
    {
        var server = new StubZipServer(nupkg)
        {
            ExposeContentRange = false
        };
        using var client = new HttpClient(server);
        var archive = await RemoteZipArchive.Open(client, "https://example/package.nupkg");
        return await NupkgParser.Parse("ThePackage", "1.2.3", archive);
    }

    [Test]
    public async Task PerPackageDefaults()
    {
        var facts = await Parse(TestNupkg.Build());

        await Assert.That(facts.BundlesSponsorCheck).IsTrue();
        await Assert.That(facts.OwnerMode).IsFalse();
        await Assert.That(facts.OwnerId).IsNull();
        await Assert.That(facts.CheckTransitive).IsFalse();
        await Assert.That(facts.PackDate).IsEqualTo("2026-01-15");
        await Assert.That(facts.LandingUrl).IsNull();
        await Assert.That(facts.Platforms.Count).IsEqualTo(1);
        await Assert.That(facts.Platforms[0].Kind).IsEqualTo(PlatformKind.GitHub);
        await Assert.That(facts.Platforms[0].Account).IsEqualTo("acmecorp");
        await Assert.That(facts.Exemptions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OwnerModeTransitive()
    {
        var facts = await Parse(TestNupkg.Build(ownerId: "acme", transitive: true));

        await Assert.That(facts.OwnerMode).IsTrue();
        await Assert.That(facts.OwnerId).IsEqualTo("acme");
        await Assert.That(facts.CheckTransitive).IsTrue();
    }

    [Test]
    public async Task ExemptionsParsed()
    {
        // The bare-string shape: what nupkgs packed before MaxTermMonths existed carry, and still
        // the majority of what is on nuget.org.
        var facts = await Parse(TestNupkg.Build(exemptions: new Dictionary<string, string>
        {
            ["Consulting"] = "Consulting clients are exempt for 6 months.",
            ["SmallRevenue"] = "Under US$10,000 annual gross revenue."
        }));

        await Assert.That(facts.Exemptions.Count).IsEqualTo(2);
        await Assert.That(facts.FindExemption("consulting")!.Message).IsEqualTo("Consulting clients are exempt for 6 months.");
        await Assert.That(facts.FindExemption("consulting")!.MaxTermMonths).IsNull();
    }

    [Test]
    public async Task BoundedExemptionsParsed()
    {
        var facts = await Parse(TestNupkg.Build(boundedExemptions: new Dictionary<string, (string, int?)>
        {
            ["Consulting"] = ("Consulting clients are exempt for 6 months.", 6),
            ["SmallRevenue"] = ("Under US$10,000 annual gross revenue.", null)
        }));

        await Assert.That(facts.Exemptions.Count).IsEqualTo(2);
        await Assert.That(facts.FindExemption("consulting")!.MaxTermMonths).IsEqualTo(6);
        await Assert.That(facts.FindExemption("consulting")!.Message).IsEqualTo("Consulting clients are exempt for 6 months.");
        await Assert.That(facts.FindExemption("smallrevenue")!.MaxTermMonths).IsNull();
    }

    [Test]
    public async Task SeveritiesAndLandingUrl()
    {
        var facts = await Parse(TestNupkg.Build(
            landingUrl: "https://acme.example.com/sponsor",
            severities: new Dictionary<string, string> { ["SC005"] = "error", ["SC023"] = "error" }));

        await Assert.That(facts.LandingUrl).IsEqualTo("https://acme.example.com/sponsor");
        await Assert.That(facts.Severities["SC005"]).IsEqualTo("error");
        await Assert.That(facts.Severities["SC023"]).IsEqualTo("error");
    }

    [Test]
    public async Task UnknownPlatformSkipped()
    {
        var facts = await Parse(TestNupkg.Build(accounts: new Dictionary<string, string>
        {
            ["SomeFuturePlatform"] = "whoever",
            ["Polar"] = "acme"
        }));

        await Assert.That(facts.Platforms.Count).IsEqualTo(1);
        await Assert.That(facts.Platforms[0].Kind).IsEqualTo(PlatformKind.Polar);
    }

    [Test]
    public async Task SponsorHashesAreReadAndMatchable()
    {
        // The whole point of reading the list: an account the consumer types can be answered against
        // the same hashes the verifier will consult, before the build rather than after it.
        var facts = await Parse(TestNupkg.Build(
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]));

        await Assert.That(facts.SponsorHashes).IsNotNull();
        await Assert.That(facts.Bundles("GitHubSponsors", "alice")).IsTrue();
        // Folded on the way in, as the bundler folded it on the way out.
        await Assert.That(facts.Bundles("GitHubSponsors", "  Alice ")).IsTrue();
        await Assert.That(facts.Bundles("GitHubSponsors", "bob")).IsFalse();
        // The same account on another platform is a different sponsorship.
        await Assert.That(facts.Bundles("OpenCollective", "alice")).IsFalse();
    }

    [Test]
    public async Task ABlankAccountHasNoAnswer() =>
        await Assert.That((await Parse(TestNupkg.Build())).Bundles("GitHubSponsors", "  ")).IsNull();

    [Test]
    public async Task AnOversizedHashListIsNotRead()
    {
        // Past the cap the file stops being worth downloading, and the wizard has to fall back to
        // giving no answer rather than to guessing one. Null, never empty: an empty set would read as
        // "this package bundles nobody" and tell every sponsor they are missing.
        var lines = (int)(NupkgParser.MaxSponsorHashBytes / 13) + 1000;
        var facts = await Parse(TestNupkg.Build(
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")],
            hashPaddingLines: lines));

        await Assert.That(facts.BundlesSponsorCheck).IsTrue();
        await Assert.That(facts.SponsorHashes).IsNull();
        await Assert.That(facts.Bundles("GitHubSponsors", "alice")).IsNull();
    }

    [Test]
    public async Task NoSponsorCheckFiles()
    {
        var facts = await Parse(TestNupkg.Build(sponsorCheck: false));

        await Assert.That(facts.BundlesSponsorCheck).IsFalse();
        await Assert.That(facts.Platforms.Count).IsEqualTo(0);
    }
}
