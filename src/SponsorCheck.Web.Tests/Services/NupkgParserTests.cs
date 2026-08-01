namespace SponsorCheck.Web.Tests.Services;

public class NupkgParserTests
{
    static PackageFacts Parse(byte[] nupkg)
    {
        using var stream = new MemoryStream(nupkg);
        return NupkgParser.Parse("ThePackage", "1.2.3", stream);
    }

    [Test]
    public async Task PerPackageDefaults()
    {
        var facts = Parse(TestNupkg.Build());

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
        var facts = Parse(TestNupkg.Build(ownerId: "acme", transitive: true));

        await Assert.That(facts.OwnerMode).IsTrue();
        await Assert.That(facts.OwnerId).IsEqualTo("acme");
        await Assert.That(facts.CheckTransitive).IsTrue();
    }

    [Test]
    public async Task ExemptionsParsed()
    {
        var facts = Parse(TestNupkg.Build(exemptions: new Dictionary<string, string>
        {
            ["Consulting"] = "Consulting clients are exempt for 6 months.",
            ["SmallRevenue"] = "Under US$10,000 annual gross revenue."
        }));

        await Assert.That(facts.Exemptions.Count).IsEqualTo(2);
        await Assert.That(facts.FindExemption("consulting")!.Message).IsEqualTo("Consulting clients are exempt for 6 months.");
    }

    [Test]
    public async Task SeveritiesAndLandingUrl()
    {
        var facts = Parse(TestNupkg.Build(
            landingUrl: "https://acme.example.com/sponsor",
            severities: new Dictionary<string, string> { ["SC005"] = "error", ["SC023"] = "error" }));

        await Assert.That(facts.LandingUrl).IsEqualTo("https://acme.example.com/sponsor");
        await Assert.That(facts.Severities["SC005"]).IsEqualTo("error");
        await Assert.That(facts.Severities["SC023"]).IsEqualTo("error");
    }

    [Test]
    public async Task UnknownPlatformSkipped()
    {
        var facts = Parse(TestNupkg.Build(accounts: new Dictionary<string, string>
        {
            ["SomeFuturePlatform"] = "whoever",
            ["Polar"] = "acme"
        }));

        await Assert.That(facts.Platforms.Count).IsEqualTo(1);
        await Assert.That(facts.Platforms[0].Kind).IsEqualTo(PlatformKind.Polar);
    }

    [Test]
    public async Task NoSponsorCheckFiles()
    {
        var facts = Parse(TestNupkg.Build(sponsorCheck: false));

        await Assert.That(facts.BundlesSponsorCheck).IsFalse();
        await Assert.That(facts.Platforms.Count).IsEqualTo(0);
    }
}
