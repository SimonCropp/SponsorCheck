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
        await cut.Find("#licensedUntil").InputAsync("2027-06");
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

    sealed class StubNuGetHandler(byte[] nupkg, string version) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/index.json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"versions\":[\"{version}\"]}}")
                });
            }

            if (url.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(nupkg)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
