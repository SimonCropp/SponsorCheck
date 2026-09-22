namespace SponsorCheck.Web.Tests.Components;

/// <summary>
/// The step mutates a model its parent page owns, and a DOM event handled in a child re-renders only
/// the child, so every input has to raise Changed or the page's Next button goes stale. The page tests
/// only walk some of the inputs; these cover each one.
/// </summary>
public class LicenseModeStepTests : WebTestContext
{
    int changed;

    IRenderedComponent<LicenseModeStep> RenderStep(ConsumerModel model) =>
        Render<LicenseModeStep>(_ => _
            .Add(_ => _.Model, model)
            .Add(_ => _.Changed, () => changed++));

    [Test]
    public async Task RenderingDoesNotNotify()
    {
        var cut = RenderStep(new());

        await Assert.That(changed).IsEqualTo(0);
        // nothing looked up, so the exemption card is offered too
        await Assert.That(cut.FindAll("button.mode-card").Count).IsEqualTo(4);
    }

    [Test]
    public async Task SelectingAModeNotifies()
    {
        var model = new ConsumerModel();
        var cut = RenderStep(model);

        await cut.FindAll("button.mode-card")[0].ClickAsync();

        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(model.Mode).IsEqualTo(ConsumerLicenseMode.Sponsor);
    }

    [Test]
    public async Task EverySponsorInputNotifies()
    {
        var cut = RenderStep(new());

        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");
        await cut.Find("#startedAfter").ChangeAsync(true);
        await cut.Find("#sponsorshipStart").InputAsync("2026-02-01");
        await cut.Find("#privateSponsorship").ChangeAsync(true);
        await cut.Find("#privateUntil").InputAsync("2027-01");

        await Assert.That(changed).IsEqualTo(7);
    }

    [Test]
    public async Task LicenseAndExemptionInputsNotify()
    {
        var cut = RenderStep(new());

        await cut.FindAll("button.mode-card")[1].ClickAsync();
        await cut.Find("#licensedUntil").InputAsync("2027-01");
        await Assert.That(changed).IsEqualTo(2);

        await cut.FindAll("button.mode-card")[2].ClickAsync();
        await cut.Find("#exemptionName").InputAsync("Consulting");
        await cut.Find("#exemptionUntil").InputAsync("2027-01");
        await Assert.That(changed).IsEqualTo(5);
    }

    static ConsumerModel ModelWithFacts(params PackagePlatformAccount[] platforms)
    {
        var model = new ConsumerModel
        {
            PackageId = "ThePackage"
        };
        model.ApplyFacts(new(
            "ThePackage",
            "1.2.3",
            BundlesSponsorCheck: true,
            CheckTransitive: false,
            OwnerMode: false,
            OwnerId: null,
            PackDate: "2026-01-15",
            LandingUrl: null,
            Platforms: platforms,
            Exemptions: [new("Consulting", "Consulting clients are exempt for 6 months.", 6)],
            Severities: new Dictionary<string, string>(),
            PrivateSponsorMaxTermMonths: PackageFacts.DefaultPrivateSponsorMaxTermMonths));
        return model;
    }

    [Test]
    public async Task SinglePlatformRendersAsTextNotCheckbox()
    {
        var model = new ConsumerModel();
        // ticked before the lookup, and not a platform this package accepts
        model.Selection(PlatformKind.Polar).Enabled = true;
        var facts = ModelWithFacts(new PackagePlatformAccount(PlatformKind.GitHub, "acmecorp")).Facts!;
        model.ApplyFacts(facts);
        var cut = RenderStep(model);

        await cut.FindAll("button.mode-card")[0].ClickAsync();

        await Assert.That(cut.FindAll("#sponsor-GitHub").Count).IsEqualTo(0);
        await Assert.That(cut.Find(".platform-name").TextContent).IsEqualTo("GitHub Sponsors");
        await Assert.That(model.Selection(PlatformKind.GitHub).Enabled).IsTrue();
        await Assert.That(model.Selection(PlatformKind.Polar).Enabled).IsFalse();

        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");
        await Assert.That(model.HasPlatform).IsTrue();
    }

    [Test]
    public async Task MultiplePlatformsKeepCheckboxes()
    {
        var model = ModelWithFacts(new PackagePlatformAccount(PlatformKind.GitHub, "acmecorp"), new PackagePlatformAccount(PlatformKind.OpenCollective, "acme"));
        var cut = RenderStep(model);

        await cut.FindAll("button.mode-card")[0].ClickAsync();

        await Assert.That(cut.FindAll("input[type=checkbox][id^=sponsor-]").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll(".platform-name").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExemptionSelectNotifies()
    {
        // With facts the exemption name is a <select>, bound on change rather than input.
        var model = ModelWithFacts(new PackagePlatformAccount(PlatformKind.GitHub, "acmecorp"));
        var cut = RenderStep(model);

        await cut.FindAll("button.mode-card")[2].ClickAsync();
        await cut.Find("#exemptionName").ChangeAsync("Consulting");

        await Assert.That(changed).IsEqualTo(2);
        await Assert.That(model.ExemptionName).IsEqualTo("Consulting");
    }
}
