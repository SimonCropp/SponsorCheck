namespace SponsorCheck.Web.Tests.Pages;

public class ConsumerPageTests : WebTestContext
{
    static void EnterPackage(IRenderedComponent<SponsorCheck.Web.Pages.Consumer> cut, string packageId = "ThePackage")
    {
        cut.Find("#packageId").Input(packageId);
        cut.Find("button.primary").Click();           // package -> situation
    }

    [Test]
    public async Task NextDisabledUntilPackageIdEntered()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("#packageId").Input("ThePackage");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task CodeEntryPreAnswersOwnerMode()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("#scCode").Input("SC021");

        await Assert.That(cut.Find("#style-owner").HasAttribute("checked")).IsTrue();
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CodeEntryPreAnswersCpm()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("#scCode").Input("SC002");

        await Assert.That(cut.Find("#cpm-yes").HasAttribute("checked")).IsTrue();
    }

    [Test]
    public async Task AuthorCodeShowsRedirect()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("#scCode").Input("SC102");

        await Assert.That(cut.Find(".callout").TextContent).Contains("author flow");
    }

    [Test]
    public async Task UnrecognizedCodeShowsError()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("#scCode").Input("SC999");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
    }

    [Test]
    public async Task OwnerModeHidesCpmQuestion()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);

        cut.Find("#style-owner").Change(true);

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WalkthroughSponsorNonCpmReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("button.primary").Click();           // situation -> license mode

        cut.FindAll("button.mode-card")[0].Click();   // sponsor
        cut.Find("#sponsor-GitHub").Change(true);
        cut.Find("#sponsor-account-GitHub").Input("alice");
        cut.Find("button.primary").Click();           // license mode -> output

        await Assert.That(cut.Markup).Contains("PackageReference (consuming .csproj)");
        await Assert.That(cut.Markup).Contains("GitHubSponsorAccount");
        await Assert.That(cut.FindAll("button.copy-markdown").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WalkthroughOwnerLicenseReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("#style-owner").Change(true);
        cut.Find("#ownerId").Input("acme");
        cut.Find("button.primary").Click();           // situation -> license mode

        cut.FindAll("button.mode-card")[1].Click();   // private license
        cut.Find("#licensedUntil").Input("2027-06");
        cut.Find("button.primary").Click();           // license mode -> output

        await Assert.That(cut.Markup).Contains("acme_SponsorshipLicensedUntil");
    }

    [Test]
    public async Task SponsorModeRequiresAccount()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        EnterPackage(cut);

        cut.Find("button.primary").Click();           // situation -> license mode

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.FindAll("button.mode-card")[0].Click();   // sponsor

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("#sponsor-GitHub").Change(true);
        cut.Find("#sponsor-account-GitHub").Input("alice");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
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
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.lookup").Click();
        cut.WaitForState(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));

        await Assert.That(cut.Markup).Contains("Owner mode — configured once via 'acme_…' properties");
        await Assert.That(cut.Markup).Contains("Publisher-defined exemptions: Consulting");

        cut.Find("button.primary").Click();           // package -> situation

        await Assert.That(cut.Markup).Contains("owner id 'acme'");
        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(0);

        cut.Find("button.primary").Click();           // situation -> license mode
        cut.FindAll("button.mode-card")[2].Click();   // exemption

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
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.lookup").Click();
        cut.WaitForState(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));

        cut.Find("#packageId").Input("OtherPackage");

        await Assert.That(cut.Markup).DoesNotContain("Read from ThePackage");

        cut.Find("button.primary").Click();           // package -> situation, manual questions again

        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LookupFailureShowsErrorAndManualPathRemains()
    {
        // WebTestContext's default HttpClient throws on any request (no network).
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.lookup").Click();
        cut.WaitForState(() => cut.Markup.Contains("Lookup failed"));

        await Assert.That(cut.Find("button.lookup").HasAttribute("disabled")).IsFalse();

        cut.Find("button.primary").Click();           // package -> situation

        await Assert.That(cut.FindAll("#scCode").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LookupWithoutSponsorCheckFallsBackToManualQuestions()
    {
        var nupkg = TestNupkg.Build(sponsorCheck: false);
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Render<SponsorCheck.Web.Pages.Consumer>();
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.lookup").Click();
        cut.WaitForState(() => cut.Markup.Contains("No SponsorCheck files found"));

        cut.Find("button.primary").Click();           // package -> situation

        await Assert.That(cut.FindAll("#scCode").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(1);
    }

    sealed class StubNuGetHandler(byte[] nupkg, string version) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
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
