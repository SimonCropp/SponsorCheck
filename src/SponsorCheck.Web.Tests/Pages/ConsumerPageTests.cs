namespace SponsorCheck.Web.Tests.Pages;

public class ConsumerPageTests : WebTestContext
{
    static void EnterPackage(IRenderedComponent<SponsorCheck.Web.Pages.Consumer> cut, string packageId = "ThePackage")
    {
        cut.Find("#packageId").Input(packageId);
        // package -> situation
        cut.Find("button.primary").Click();
    }

    [Test]
    public async Task NextDisabledUntilPackageIdEntered()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#packageId").InputAsync("ThePackage");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task CodeEntryPreAnswersOwnerMode()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await cut.Find("#scCode").InputAsync("SC021");

        await Assert.That(cut.Find("#style-owner").HasAttribute("checked")).IsTrue();
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CodeEntryPreAnswersCpm()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await cut.Find("#scCode").InputAsync("SC002");

        await Assert.That(cut.Find("#cpm-yes").HasAttribute("checked")).IsTrue();
    }

    [Test]
    public async Task AuthorCodeShowsRedirect()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await cut.Find("#scCode").InputAsync("SC102");

        await Assert.That(cut.Find(".callout").TextContent).Contains("author flow");
    }

    [Test]
    public async Task UnrecognizedCodeShowsError()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await cut.Find("#scCode").InputAsync("SC999");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
    }

    [Test]
    public async Task OwnerModeHidesCpmQuestion()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);

        await cut.Find("#style-owner").ChangeAsync(true);

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WalkthroughSponsorNonCpmReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();

        // sponsor
        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");
        // license mode -> output
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Markup).Contains("PackageReference (consuming .csproj)");
        await Assert.That(cut.Markup).Contains("GitHubSponsorAccount");
        await Assert.That(cut.FindAll("button.copy-markdown").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WalkthroughOwnerLicenseReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await cut.Find("#style-owner").ChangeAsync(true);
        await cut.Find("#ownerId").InputAsync("acme");
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();

        // private license
        await cut.FindAll("button.mode-card")[1].ClickAsync();
        // Clock-relative: a literal month drifts out of the one-year window and would start tripping
        // the SC035 callout on its own.
        await cut.Find("#licensedUntil").InputAsync(MonthsFromNow(6));
        // license mode -> output
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Markup).Contains("acme_SponsorshipLicensedUntil");
    }

    [Test]
    public async Task SponsorModeRequiresAccount()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        // sponsor
        await cut.FindAll("button.mode-card")[0].ClickAsync();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    // The four below cover the wizard catching what it previously emitted and left for the
    // consumer's next build to reject. Every month is computed off the wall clock rather than
    // written as a literal: a fixture like "2027-06" tests the boundary for a while and then
    // silently stops, which is exactly the rot these callouts exist to prevent.

    static string MonthsFromNow(int months) => $"{DateTime.UtcNow.AddMonths(months):yyyy-MM}";

    async Task<IRenderedComponent<SponsorCheck.Web.Pages.Consumer>> SponsorModeWithAccount()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");
        return cut;
    }

    [Test]
    public async Task PrivateUntilBeyondTheCapIsCalledOut()
    {
        var cut = await SponsorModeWithAccount();
        await cut.Find("#privateSponsorship").ChangeAsync(true);
        await cut.Find("#privateUntil").InputAsync(MonthsFromNow(18));

        await Assert.That(cut.Markup).Contains("SC053");
        // A warning, not a block — the cap here is the shipped default rather than a value read from
        // this package, and refusing to advance on a guess is worse than saying it is a guess.
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task AnAlreadyExpiredPrivateUntilIsCalledOut()
    {
        var cut = await SponsorModeWithAccount();
        await cut.Find("#privateSponsorship").ChangeAsync(true);
        await cut.Find("#privateUntil").InputAsync(MonthsFromNow(-1));

        await Assert.That(cut.Markup).Contains("SC056");
    }

    [Test]
    public async Task LicensedUntilBeyondAYearIsCalledOut()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);
        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll("button.mode-card")[1].ClickAsync();
        await cut.Find("#licensedUntil").InputAsync(MonthsFromNow(18));

        await Assert.That(cut.Markup).Contains("SC035");
    }

    async Task<IRenderedComponent<SponsorCheck.Web.Pages.Consumer>> LookedUpSponsorMode(byte[] nupkg)
    {
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));
        // package -> situation
        await cut.Find("button.primary").ClickAsync();
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        return cut;
    }

    [Test]
    public async Task AnAccountInTheBundledListIsConfirmed()
    {
        var cut = await LookedUpSponsorMode(TestNupkg.Build(
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]));

        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");

        await Assert.That(cut.Markup).Contains("Found in ThePackage 1.2.3's bundled list");
        await Assert.That(cut.Markup).DoesNotContain("SC007");
    }

    [Test]
    public async Task AnAccountMissingFromTheBundledListIsAnsweredBeforeTheBuild()
    {
        // The point of the whole check: SC007 is knowable at the moment the account is typed, and so
        // are the two reasons a real sponsor lands there.
        var cut = await LookedUpSponsorMode(TestNupkg.Build(
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]));

        await cut.Find("#sponsor-account-GitHub").InputAsync("bob");

        await Assert.That(cut.Markup).Contains("Not in ThePackage 1.2.3's bundled list");
        await Assert.That(cut.Markup).Contains("SC007");
        await Assert.That(cut.Markup).Contains("private or incognito");
        // A wrong account is not a blocked wizard — the consumer may well be the recent or private
        // sponsor the callout describes, and only they can say.
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task OwnerModeReportsTheOwnerScopedNoMatchCode()
    {
        // The no-match codes run as a triple, so the callout has to name the one this consumer will
        // actually see rather than the non-CPM default.
        var cut = await LookedUpSponsorMode(TestNupkg.Build(
            ownerId: "acme",
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]));

        await cut.Find("#sponsor-account-GitHub").InputAsync("bob");

        await Assert.That(cut.Markup).Contains("SC024");
        await Assert.That(cut.Markup).DoesNotContain("SC007");
    }

    [Test]
    public async Task AnUnreadableHashListSaysNothingEitherWay()
    {
        // No answer beats a wrong one: the list was never read, so neither confirming nor denying is
        // honest here.
        var lines = (int)(NupkgParser.MaxSponsorHashBytes / 13) + 1000;
        var cut = await LookedUpSponsorMode(TestNupkg.Build(
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")],
            hashPaddingLines: lines));

        await cut.Find("#sponsor-account-GitHub").InputAsync("bob");

        await Assert.That(cut.Markup).DoesNotContain("bundled list");
    }

    [Test]
    public async Task ExemptionUntilBeyondThePublishersBoundIsCalledOut()
    {
        // The only one of these ceilings that is per-exemption rather than per-package, so it is the
        // one the wizard can only get right by reading what the publisher actually bundled.
        var nupkg = TestNupkg.Build(
            boundedExemptions: new Dictionary<string, (string, int?)>
            {
                ["Consulting"] = ("Consulting clients are exempt for 6 months.", 6)
            });
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));
        // package -> situation
        await cut.Find("button.primary").ClickAsync();
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll("button.mode-card")[2].ClickAsync();
        await cut.Find("#exemptionName").ChangeAsync("Consulting");
        // Inside the 12 month default, outside this exemption's 6 — so it only trips if the
        // publisher's own bound reached the check.
        await cut.Find("#exemptionUntil").InputAsync(MonthsFromNow(9));

        await Assert.That(cut.Markup).Contains("SC044");
        await Assert.That(cut.Markup).Contains("6-month bound");
    }

    [Test]
    public async Task PolarOnlyPackageIsNotOfferedThePrivateRoute()
    {
        // Polar supporters are billing customers rather than a published list, so nothing about a
        // Polar sponsorship is ever excluded from the bundle — offering the route would describe an
        // exclusion that does not happen, in terms of two platforms this package does not use.
        var nupkg = TestNupkg.Build(accounts: new Dictionary<string, string> { ["Polar"] = "acme" });
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));
        // package -> situation
        await cut.Find("button.primary").ClickAsync();
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll("button.mode-card")[0].ClickAsync();

        await Assert.That(cut.FindAll("#privateSponsorship").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#sponsor-Polar").Count).IsEqualTo(1);
    }

    [Test]
    public async Task StepperNavigatesBackToCompletedSteps()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(1);

        await cut.Find(".stepper .step-link").ClickAsync();

        await Assert.That(cut.FindAll("#packageId").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(0);
    }

    [Test]
    public async Task LookupDrivesOwnerModeAndExemptions()
    {
        var nupkg = TestNupkg.Build(
            ownerId: "acme",
            transitive: true,
            exemptions: new Dictionary<string, string> { ["Consulting"] = "Consulting clients are exempt for 6 months." });
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));

        await Assert.That(cut.Markup).Contains("Owner mode — configured once via 'acme_…' properties");
        await Assert.That(cut.Markup).Contains("Publisher-defined exemptions: Consulting");

        // package -> situation
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Markup).Contains("owner id 'acme'");
        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(0);

        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        // exemption
        await cut.FindAll("button.mode-card")[2].ClickAsync();

        var options = cut.FindAll("#exemptionName option");
        await Assert.That(options.Count).IsEqualTo(2);
        await Assert.That(options[1].TextContent).IsEqualTo("Consulting");
    }

    [Test]
    public async Task EditingPackageIdentityClearsFacts()
    {
        var nupkg = TestNupkg.Build(ownerId: "acme");
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));

        await cut.Find("#packageId").InputAsync("OtherPackage");

        await Assert.That(cut.Markup).DoesNotContain("Read from ThePackage");

        // package -> situation, manual questions again
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LookupFailureShowsErrorAndManualPathRemains()
    {
        // WebTestContext's default HttpClient throws on any request (no network).
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Lookup failed"));

        await Assert.That(cut.Find("button.lookup").HasAttribute("disabled")).IsFalse();

        // package -> situation
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.FindAll("#scCode").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LookupWithoutSponsorCheckFallsBackToManualQuestions()
    {
        var nupkg = TestNupkg.Build(sponsorCheck: false);
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        await cut.Find("#packageId").InputAsync("ThePackage");
        await cut.Find("button.lookup").ClickAsync();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("No SponsorCheck files found"));

        // package -> situation
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.FindAll("#scCode").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(1);
    }
}
