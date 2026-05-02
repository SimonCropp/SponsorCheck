namespace EnforceOssSponsorship.Tests;

public class LicenseModeResolverTests
{
    static IReadOnlyDictionary<string, string?> NoSponsors() => new Dictionary<string, string?>
    {
        ["GitHubSponsors"] = null,
        ["OpenCollective"] = null,
        ["Polar"] = null
    };

    static IReadOnlyDictionary<string, string?> Sponsors(params (string platform, string? account)[] entries)
    {
        var dict = new Dictionary<string, string?>(NoSponsors());
        foreach (var e in entries)
        {
            dict[e.platform] = e.account;
        }

        return dict;
    }

    [Test]
    public async Task NothingSet_MissingConfig()
    {
        var d = LicenseModeResolver.Resolve(null, null, NoSponsors(), "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
        await Assert.That(d.PackageId).IsEqualTo("ThePkg");
    }

    [Test]
    public async Task IgnoredOnly_Ignored()
    {
        var d = LicenseModeResolver.Resolve("true", null, NoSponsors(), "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.Ignored>();
    }

    [Test]
    public async Task IgnoredFalse_TreatsAsUnset()
    {
        var d = LicenseModeResolver.Resolve("false", null, NoSponsors(), "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task SponsorOnly_Sponsor()
    {
        var d = LicenseModeResolver.Resolve(null, null, Sponsors(("GitHubSponsors", "alice")), "ThePkg");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.AccountByPlatform.Count).IsEqualTo(1);
        await Assert.That(sponsor.AccountByPlatform["GitHubSponsors"]).IsEqualTo("alice");
    }

    [Test]
    public async Task MultipleSponsorPlatforms_Sponsor()
    {
        var d = LicenseModeResolver.Resolve(null, null,
            Sponsors(("GitHubSponsors", "alice"), ("Polar", "alice-co")), "ThePkg");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.AccountByPlatform.Count).IsEqualTo(2);
    }

    [Test]
    public async Task LicenseOnly_Licensed()
    {
        var d = LicenseModeResolver.Resolve(null, "2099-12", NoSponsors(), "ThePkg");
        var licensed = (LicenseDecision.Licensed)d;
        await Assert.That(licensed.LicensedUntilRaw).IsEqualTo("2099-12");
    }

    [Test]
    public async Task IgnoredPlusSponsor_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", null, Sponsors(("GitHubSponsors", "alice")), "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes).Contains("SponsorshipIgnored");
        await Assert.That(conflict.Modes).Contains("Sponsor");
    }

    [Test]
    public async Task SponsorPlusLicense_Conflict()
    {
        var d = LicenseModeResolver.Resolve(null, "2099-12", Sponsors(("GitHubSponsors", "alice")), "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.ConflictingModes>();
    }

    [Test]
    public async Task AllThree_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", "2099-12", Sponsors(("Polar", "alice")), "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task EmptySponsorValuesIgnored()
    {
        var d = LicenseModeResolver.Resolve(null, null,
            Sponsors(("GitHubSponsors", "  "), ("Polar", null)), "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }
}
