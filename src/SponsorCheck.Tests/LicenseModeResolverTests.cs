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
        var d = LicenseModeResolver.Resolve(null, null, null, null, NoSponsors(), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
        await Assert.That(d.PackageId).IsEqualTo("ThePkg");
    }

    [Test]
    public async Task IgnoredOnly_Ignored()
    {
        var d = LicenseModeResolver.Resolve("true", null, null, null, NoSponsors(), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.Ignored>();
    }

    [Test]
    public async Task IgnoredFalse_TreatsAsUnset()
    {
        var d = LicenseModeResolver.Resolve("false", null, null, null, NoSponsors(), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task SponsorOnly_Sponsor()
    {
        var d = LicenseModeResolver.Resolve(null, null, null, null, Sponsors(("GitHubSponsors", "alice")), null, "ThePkg");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.AccountByPlatform.Count).IsEqualTo(1);
        await Assert.That(sponsor.AccountByPlatform["GitHubSponsors"]).IsEqualTo("alice");
    }

    [Test]
    public async Task MultipleSponsorPlatforms_Sponsor()
    {
        var d = LicenseModeResolver.Resolve(null, null, null,
            null, Sponsors(("GitHubSponsors", "alice"), ("Polar", "alice-co")), null, "ThePkg");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.AccountByPlatform.Count).IsEqualTo(2);
    }

    [Test]
    public async Task LicenseOnly_Licensed()
    {
        var d = LicenseModeResolver.Resolve(null, "2099-12", null, null, NoSponsors(), null, "ThePkg");
        var licensed = (LicenseDecision.Licensed)d;
        await Assert.That(licensed.LicensedUntilRaw).IsEqualTo("2099-12");
    }

    [Test]
    public async Task IgnoredPlusSponsor_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", null, null, null, Sponsors(("GitHubSponsors", "alice")), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes).Contains("SponsorshipLicenseIgnored");
        await Assert.That(conflict.Modes).Contains("Sponsor");
    }

    [Test]
    public async Task SponsorPlusLicense_Conflict()
    {
        var d = LicenseModeResolver.Resolve(null, "2099-12", null, null, Sponsors(("GitHubSponsors", "alice")), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.ConflictingModes>();
    }

    [Test]
    public async Task AllThree_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", "2099-12", null, null, Sponsors(("Polar", "alice")), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task EmptySponsorValuesIgnored()
    {
        var d = LicenseModeResolver.Resolve(null, null, null,
            null, Sponsors(("GitHubSponsors", "  "), ("Polar", null)), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task ExemptionOnly_Exempt()
    {
        var d = LicenseModeResolver.Resolve(null, null, "Consulting", null, NoSponsors(), null, "ThePkg");
        var exempt = (LicenseDecision.Exempt)d;
        await Assert.That(exempt.ExemptionName).IsEqualTo("Consulting");
    }

    [Test]
    public async Task ExemptionWithUntil_CarriesUntilTrimmed()
    {
        var d = LicenseModeResolver.Resolve(null, null, "Consulting", "  2027-02  ", NoSponsors(), null, "ThePkg");
        var exempt = (LicenseDecision.Exempt)d;
        await Assert.That(exempt.ExemptionUntilRaw).IsEqualTo("2027-02");
    }

    [Test]
    public async Task ExemptionWithBlankUntil_IsNull()
    {
        var d = LicenseModeResolver.Resolve(null, null, "Consulting", "   ", NoSponsors(), null, "ThePkg");
        var exempt = (LicenseDecision.Exempt)d;
        await Assert.That(exempt.ExemptionUntilRaw).IsNull();
    }

    [Test]
    public async Task ExemptionUntilAlone_IsNotAMode()
    {
        // Mirrors SponsorshipStart: the until date qualifies a claim, it never selects one. On its
        // own it must leave the consumer with the "no license mode set" diagnostic, not an Exempt
        // decision naming an empty exemption.
        var d = LicenseModeResolver.Resolve(null, null, null, "2027-02", NoSponsors(), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task EmptyExemption_TreatedAsUnset()
    {
        var d = LicenseModeResolver.Resolve(null, null, "  ", null, NoSponsors(), null, "ThePkg");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task ExemptionPlusSponsor_Conflict()
    {
        var d = LicenseModeResolver.Resolve(null, null, "Consulting", null, Sponsors(("GitHubSponsors", "alice")), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes).Contains("SponsorshipExemption");
        await Assert.That(conflict.Modes).Contains("Sponsor");
    }

    [Test]
    public async Task ExemptionPlusLicense_Conflict()
    {
        var d = LicenseModeResolver.Resolve(null, "2099-12", "Consulting", null, NoSponsors(), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes).Contains("SponsorshipExemption");
        await Assert.That(conflict.Modes).Contains("SponsorshipLicensedUntil");
    }

    [Test]
    public async Task ExemptionPlusIgnored_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", null, "Consulting", null, NoSponsors(), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes).Contains("SponsorshipExemption");
        await Assert.That(conflict.Modes).Contains("SponsorshipLicenseIgnored");
    }

    [Test]
    public async Task AllFour_Conflict()
    {
        var d = LicenseModeResolver.Resolve("true", "2099-12", "Consulting", null, Sponsors(("Polar", "alice")), null, "ThePkg");
        var conflict = (LicenseDecision.ConflictingModes)d;
        await Assert.That(conflict.Modes.Count).IsEqualTo(4);
    }

    [Test]
    public async Task PrivateUntilWithSponsor_CarriesUntilTrimmed()
    {
        var d = LicenseModeResolver.Resolve(null, null, null, null, Sponsors(("GitHubSponsors", "dave")), null, "ThePkg", "  2027-02  ");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.PrivateUntilRaw).IsEqualTo("2027-02");
    }

    [Test]
    public async Task PrivateUntilBlank_IsNull()
    {
        var d = LicenseModeResolver.Resolve(null, null, null, null, Sponsors(("GitHubSponsors", "dave")), null, "ThePkg", "   ");
        var sponsor = (LicenseDecision.Sponsor)d;
        await Assert.That(sponsor.PrivateUntilRaw).IsNull();
    }

    [Test]
    public async Task PrivateUntilAlone_IsNotAMode()
    {
        // Same rule as SponsorshipExemptionUntil and SponsorshipStart: it qualifies a sponsor claim
        // and never selects one, so on its own the consumer still gets "no license mode set" rather
        // than a Sponsor decision with no account to attest to.
        var d = LicenseModeResolver.Resolve(null, null, null, null, NoSponsors(), null, "ThePkg", "2027-02");
        await Assert.That(d).IsTypeOf<LicenseDecision.MissingConfig>();
    }

    [Test]
    public async Task PrivateUntilDoesNotConflictWithSponsorMode()
    {
        // A qualifier must not count towards the mode tally — pairing it with a sponsor account is
        // the whole point, so it must not read as two modes and trip SC003.
        var d = LicenseModeResolver.Resolve(null, null, null, null, Sponsors(("GitHubSponsors", "dave")), null, "ThePkg", "2027-02");
        await Assert.That(d).IsTypeOf<LicenseDecision.Sponsor>();
    }
}
