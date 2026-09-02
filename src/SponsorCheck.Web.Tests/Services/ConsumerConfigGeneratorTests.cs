namespace SponsorCheck.Web.Tests.Services;

public class ConsumerConfigGeneratorTests
{
    static ConsumerModel BaseModel(Placement placement)
    {
        var model = new ConsumerModel
        {
            PackageId = "ThePackage",
            PackageVersion = "1.2.3"
        };
        switch (placement)
        {
            case Placement.PerPackageCpm:
                model.Cpm = true;
                break;
            case Placement.OwnerMode:
                model.OwnerMode = true;
                model.OwnerId = "acme";
                break;
        }

        return model;
    }

    static void Sponsor(ConsumerModel model, PlatformKind kind, string account)
    {
        model.Mode = ConsumerLicenseMode.Sponsor;
        var selection = model.Selection(kind);
        selection.Enabled = true;
        selection.Account = account;
    }

    static string Dump(ConsumerModel model)
    {
        var output = ConsumerConfigGenerator.Generate(model);
        var builder = new StringBuilder();
        builder.AppendLine($"=== Snippet ({output.SnippetTitle}) ===");
        builder.AppendLine(output.Snippet);
        builder.AppendLine();
        builder.AppendLine("=== File to edit ===");
        builder.AppendLine(output.FileToEdit);
        builder.AppendLine();
        builder.AppendLine("=== Instruction ===");
        builder.AppendLine(output.Instruction);
        builder.AppendLine();
        builder.AppendLine("=== Build outcome ===");
        builder.AppendLine(output.BuildOutcome);
        builder.AppendLine();
        builder.AppendLine("=== Notes ===");
        foreach (var note in output.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("=== Markdown ===");
        builder.AppendLine(output.Markdown);
        return builder.ToString();
    }

    [Test]
    public async Task ProjectSponsorSingle()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectSponsorWithStart()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "carol");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-04-30";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectSponsorMultiPlatform()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "acmecorp");
        Sponsor(model, PlatformKind.OpenCollective, "acme-org");
        Sponsor(model, PlatformKind.Polar, "acme");
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectLicense()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectExemption()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectIgnore()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmSponsor()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmLicense()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmExemption()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "SmallRevenue";
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmIgnore()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerSponsor()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerSponsorWithStart()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "carol");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-04-30";
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerLicense()
    {
        var model = BaseModel(Placement.OwnerMode);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerIgnore()
    {
        var model = BaseModel(Placement.OwnerMode);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }

    static PackageFacts Facts(
        bool transitive = false,
        string? packDate = "2026-01-15",
        IReadOnlyList<PackageExemption>? exemptions = null,
        IReadOnlyDictionary<string, string>? severities = null,
        int privateSponsorMaxTermMonths = PackageFacts.DefaultPrivateSponsorMaxTermMonths,
        IReadOnlyList<PackagePlatformAccount>? platforms = null,
        IReadOnlyList<string>? sponsorHashes = null) =>
        new(
            "ThePackage",
            "1.2.3",
            BundlesSponsorCheck: true,
            CheckTransitive: transitive,
            OwnerMode: false,
            OwnerId: null,
            PackDate: packDate,
            LandingUrl: null,
            Platforms: platforms ?? [new(PlatformKind.GitHub, "acmecorp")],
            Exemptions: exemptions ?? [],
            Severities: severities ?? new Dictionary<string, string>(),
            PrivateSponsorMaxTermMonths: privateSponsorMaxTermMonths,
            // Null rather than empty when a test doesn't care: that is the "list not read" state
            // every pre-existing test here is written against.
            SponsorHashes: sponsorHashes is null ? null : new HashSet<string>(sponsorHashes, StringComparer.Ordinal));

    [Test]
    public async Task FactsExemptionSelected()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "consulting";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.")]);
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsBoundedExemptionSelected()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        model.ExemptionUntilMonth = "2027-02";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.", 6)]);
        await Assert.That(model.IsExemptionUntilRequired).IsTrue();
        await Assert.That(model.ModeComplete).IsTrue();
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsBoundedExemptionMissingUntil_IsIncomplete()
    {
        // The wizard has to block on the missing end month rather than emit a snippet the
        // consumer's next build rejects with SC038.
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.", 6)]);
        await Assert.That(model.IsExemptionUntilRequired).IsTrue();
        await Assert.That(model.ModeComplete).IsFalse();
    }

    [Test]
    public async Task FactsUncappedExemptionWithSelfImposedUntil()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        model.ExemptionUntilMonth = "2027-02";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.")]);
        await Assert.That(model.IsExemptionUntilRequired).IsFalse();
        await Assert.That(model.ModeComplete).IsTrue();
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerBoundedExemption()
    {
        var model = BaseModel(Placement.OwnerMode);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        model.ExemptionUntilMonth = "2027-02";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.", 6)]);
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsExemptionUnknown()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Enterprise";
        model.Facts = Facts(exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.")]);
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsIgnoreEscalated()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Ignore;
        model.Facts = Facts(severities: new Dictionary<string, string> { ["SC005"] = "error" });
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsStartOnOrBeforePackDate()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "carol");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-01-10";
        model.Facts = Facts();
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsSponsorTransitiveWithPackDate()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "alice");
        model.Facts = Facts(transitive: true);
        await Verify(Dump(model));
    }

    static void PrivateSponsor(ConsumerModel model, string until = "2027-05")
    {
        model.PrivateSponsorship = true;
        model.PrivateUntilMonth = until;
    }

    [Test]
    public async Task ProjectPrivateSponsor()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "octocat");
        PrivateSponsor(model);
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmPrivateSponsor()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        Sponsor(model, PlatformKind.OpenCollective, "octocat");
        PrivateSponsor(model);
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerPrivateSponsor()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "octocat");
        PrivateSponsor(model);
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsPrivateSponsorWithNarrowedCap()
    {
        // The cap is read out of the inspected package, so the generated outcome text names the
        // publisher's number rather than the shipped default.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "octocat");
        PrivateSponsor(model, "2026-06");
        model.Facts = Facts(privateSponsorMaxTermMonths: 6);
        await Verify(Dump(model));
    }

    [Test]
    public async Task PrivateSponsorTakesPrecedenceOverStart()
    {
        // Both qualifiers set: the private declaration decides the build, so the outcome text has to
        // describe that path rather than the SponsorshipStart one.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "octocat");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-04-30";
        PrivateSponsor(model);
        await Verify(Dump(model));
    }

    [Test]
    public async Task FactsAccountKnownMissingFromTheBundledList()
    {
        // The list was read, so the outcome is not a forecast. Telling someone the build "passes when
        // the account was in the list" while holding proof that it is not would be the wizard sitting
        // on the answer.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "bob");
        model.Facts = Facts(sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]);
        await Assert.That(model.NoEnteredAccountIsBundled).IsTrue();
        await Verify(Dump(model));
    }

    [Test]
    public async Task AKnownGoodAccountKeepsTheForecastWording()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "alice");
        model.Facts = Facts(sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]);
        await Assert.That(model.NoEnteredAccountIsBundled).IsFalse();
    }

    [Test]
    public async Task AVersionEditedAfterLookupWithdrawsTheHashAnswer()
    {
        // The facts describe the version that was inspected. Once the consumer points the build at a
        // different one they describe a different package, and "that account is not a sponsor" is a
        // claim far too confident to make about a package never looked at.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "bob");
        model.Facts = Facts(sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]);
        await Assert.That(model.NoEnteredAccountIsBundled).IsTrue();

        model.PackageVersion = "2.0.0";

        await Assert.That(model.BundlesAccount(Platform.GitHub)).IsNull();
        await Assert.That(model.NoEnteredAccountIsBundled).IsFalse();
    }

    [Test]
    public async Task LookupOfAPolarOnlyPackageDropsAPrivateAnswer()
    {
        // A consumer can tick the private box before looking the package up. If the lookup then shows
        // a package with no private-capable platform the wizard stops offering the route, so the
        // stale answer must go with it rather than surviving as an attribute nothing on screen
        // explains.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.Polar, "acme");
        PrivateSponsor(model);
        await Assert.That(model.PrivateDeclared).IsTrue();

        model.ApplyFacts(Facts(platforms: [new(PlatformKind.Polar, "acme")]));

        await Assert.That(model.PrivateDeclared).IsFalse();
        await Assert.That(ConsumerConfigGenerator.Generate(model).Snippet).DoesNotContain("SponsorshipPrivateUntil");
    }

    [Test]
    public async Task PrivateSponsorWithoutAMonthIsIncomplete()
    {
        // SponsorshipPrivateUntil qualifies a sponsor claim rather than being one, so a checked box
        // with no month is not a usable configuration.
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "octocat");
        model.PrivateSponsorship = true;
        await Assert.That(model.IsComplete).IsFalse();

        model.PrivateUntilMonth = "May 2027";
        await Assert.That(model.IsComplete).IsFalse();

        model.PrivateUntilMonth = "2027-05";
        await Assert.That(model.IsComplete).IsTrue();
    }

    [Test]
    public async Task WizardUrlOnlyChangesTheAttributionLine()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "alice");
        var packageUrl = WizardLinks.Package("ThePackage");

        var generic = ConsumerConfigGenerator.Generate(model);
        var deepLinked = ConsumerConfigGenerator.Generate(model, packageUrl);

        await Assert.That(deepLinked.Markdown).Contains($"Generated by the [SponsorCheck setup wizard]({packageUrl}).");
        await Assert.That(deepLinked.Markdown.Replace(packageUrl, WizardLinks.Consumer)).IsEqualTo(generic.Markdown);
        await Assert.That(deepLinked.SnippetTitle).IsEqualTo(generic.SnippetTitle);
        await Assert.That(deepLinked.Snippet).IsEqualTo(generic.Snippet);
        await Assert.That(deepLinked.FileToEdit).IsEqualTo(generic.FileToEdit);
        await Assert.That(deepLinked.Instruction).IsEqualTo(generic.Instruction);
        await Assert.That(deepLinked.BuildOutcome).IsEqualTo(generic.BuildOutcome);
        await Assert.That(string.Join("\n", deepLinked.Notes)).IsEqualTo(string.Join("\n", generic.Notes));
    }
}
