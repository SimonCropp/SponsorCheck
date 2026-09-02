namespace SponsorCheck.Web.Tests.Pages;

public class PackagePageTests : WebTestContext
{
    IRenderedComponent<SponsorCheck.Web.Pages.Package> Open(string id = "ThePackage") =>
        Render<SponsorCheck.Web.Pages.Package>(_ => _.Add(_ => _.PackageId, id));

    /// <summary>Lands on the page with the feed stubbed and waits for the lookup to complete.</summary>
    async Task<(IRenderedComponent<SponsorCheck.Web.Pages.Package> Cut, StubNuGetHandler Stub)> Land(
        byte[] nupkg,
        string id = "ThePackage",
        string version = "1.2.3")
    {
        var stub = new StubNuGetHandler(nupkg, version);
        Services.AddScoped(_ => new HttpClient(stub));
        var cut = Open(id);
        await cut.WaitForStateAsync(() => cut.Markup.Contains($"Read from {id} {version}"));
        return (cut, stub);
    }

    static List<string> NavButtons(IRenderedComponent<SponsorCheck.Web.Pages.Package> cut) =>
        cut.FindAll(".wizard-nav button").Select(_ => _.TextContent.Trim()).ToList();

    static Task SponsorAlice(IRenderedComponent<SponsorCheck.Web.Pages.Package> cut) =>
        SponsorAs(cut, "alice");

    static async Task SponsorAs(IRenderedComponent<SponsorCheck.Web.Pages.Package> cut, string account)
    {
        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync(account);
    }

    [Test]
    public async Task OwnerModeLandsOnLicenseMode()
    {
        var (cut, _) = await Land(TestNupkg.Build(ownerId: "acme"));

        await Assert.That(cut.FindAll(".stepper li").Count).IsEqualTo(2);
        await Assert.That(cut.Find(".stepper li.active").TextContent).Contains("License mode");
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(1);
        // no exemption card: the package defines none
        await Assert.That(cut.FindAll("button.mode-card").Count).IsEqualTo(3);
        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#scCode").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("Owner mode — configured once via 'acme_…' properties");
        await Assert.That(NavButtons(cut).Contains("Back")).IsFalse();
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task PerPackageLandsOnSituation()
    {
        var (cut, _) = await Land(TestNupkg.Build());

        await Assert.That(cut.FindAll(".stepper li").Count).IsEqualTo(3);
        await Assert.That(cut.Find(".stepper li.active").TextContent).Contains("Situation");
        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);
        await Assert.That(cut.Find("#cpm-no").HasAttribute("checked")).IsTrue();
        await Assert.That(cut.FindAll("#style-owner").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("per-package configuration");
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task NextIsGatedByTheLicenseModeStep()
    {
        // Every mutation here happens inside LicenseModeStep, but the disabled attribute is rendered
        // by the page from ModeComplete, so it only tracks the model if the child notifies the page.
        var (cut, _) = await Land(TestNupkg.Build(ownerId: "acme"));

        await cut.FindAll("button.mode-card")[0].ClickAsync();
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#sponsor-GitHub").ChangeAsync(true);
        await cut.Find("#sponsor-account-GitHub").InputAsync("alice");
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();

        await cut.Find("#startedAfter").ChangeAsync(true);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#sponsorshipStart").InputAsync("2026-02-01");
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task OwnerWalkthroughReachesOutput()
    {
        var (cut, _) = await Land(TestNupkg.Build(ownerId: "acme"));
        await SponsorAlice(cut);
        // license mode -> output
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Markup).Contains("Global properties (owner mode)");
        await Assert.That(cut.Markup).Contains("acme_GitHubSponsorAccount");
        await Assert.That(cut.Markup).Contains("Directory.Build.props");
        await Assert.That(cut.Find(".stepper li.active").TextContent).Contains("Output");
        await Assert.That(cut.FindAll("button.copy-markdown").Count).IsEqualTo(1);
        await Assert.That(NavButtons(cut).Contains("Start over")).IsTrue();
    }

    [Test]
    public async Task CopiedMarkdownLinksThePackageWizard()
    {
        var (cut, _) = await Land(TestNupkg.Build(ownerId: "acme"));
        await SponsorAlice(cut);
        await cut.Find("button.primary").ClickAsync();

        await cut.Find("button.copy-markdown").ClickAsync();

        var invocation = JSInterop.Invocations.Single(_ => _.Identifier == "sponsorCheck.copyToClipboard");
        var copied = (string) invocation.Arguments[0]!;
        await Assert.That(copied).StartsWith("# Configure SponsorCheck licensing for ThePackage");
        await Assert.That(copied).Contains("Generated by the [SponsorCheck setup wizard](https://simoncropp.github.io/SponsorCheck/package/ThePackage).");
        // the resolved version made it all the way through
        await Assert.That(copied).Contains("version 1.2.3, packed 2026-01-15, transitive checking off");
        await Assert.That(copied).DoesNotContain("/SponsorCheck/consumer)");
    }

    [Test]
    public async Task PerPackageCpmWalkthroughReachesOutput()
    {
        var (cut, _) = await Land(TestNupkg.Build());
        await cut.Find("#cpm-yes").ChangeAsync(true);
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();
        await SponsorAlice(cut);
        // license mode -> output
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.Markup).Contains("PackageVersion (Directory.Packages.props)");
        await Assert.That(cut.Markup).Contains("Version=\"1.2.3\"");
    }

    [Test]
    public async Task LookupFailureLinksToTheConsumerFlow()
    {
        // WebTestContext's default HttpClient throws on any request (no network).
        var cut = Open();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Lookup failed"));

        await Assert.That(cut.FindAll("a[href='consumer']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll(".facts-summary").Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnknownPackageReportsNotFound()
    {
        Services.AddScoped(_ => new HttpClient(StubNuGetHandler.NotFound()));

        var cut = Open("Nope");
        await cut.WaitForStateAsync(() => cut.Markup.Contains("was not found on nuget.org"));

        // the lookup's own message, not the generic failure wrapper
        await Assert.That(cut.Markup).Contains("Package 'Nope' was not found on nuget.org.");
        await Assert.That(cut.Markup).DoesNotContain("Lookup failed");
        await Assert.That(cut.FindAll("a[href='consumer']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task NewestVersionWithoutSponsorCheckExplainsAndLinksOut()
    {
        var nupkg = TestNupkg.Build(sponsorCheck: false);
        Services.AddScoped(_ => new HttpClient(new StubNuGetHandler(nupkg, "1.2.3")));

        var cut = Open();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("No SponsorCheck files found"));

        var callout = cut.Find(".callout").TextContent;
        await Assert.That(callout).Contains("ThePackage");
        await Assert.That(callout).Contains("1.2.3");
        await Assert.That(cut.FindAll("a[href='consumer']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll(".facts-summary").Count).IsEqualTo(0);
    }

    [Test]
    public async Task LoadingStateWhileTheLookupIsPending()
    {
        var gate = new GatedHandler(new StubNuGetHandler(TestNupkg.Build(ownerId: "acme"), "1.2.3"));
        Services.AddScoped(_ => new HttpClient(gate));

        var cut = Open();

        await Assert.That(cut.Markup).Contains("Inspecting");
        await Assert.That(cut.Markup).Contains("nuget.org");
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("button.primary").Count).IsEqualTo(0);

        gate.Release();
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from ThePackage 1.2.3"));

        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("Inspecting");
    }

    [Test]
    public async Task StartOverKeepsTheFactsAndReturnsToTheFirstStep()
    {
        var (cut, stub) = await Land(TestNupkg.Build(ownerId: "acme"));
        await SponsorAlice(cut);
        await cut.Find("button.primary").ClickAsync();
        var requests = stub.Requests.Count;

        await cut.FindAll(".wizard-nav button").Single(_ => _.TextContent.Trim() == "Start over").ClickAsync();

        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".mode-card.selected").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Read from ThePackage 1.2.3");
        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(0);
        // the facts are kept, not fetched again
        await Assert.That(stub.Requests.Count).IsEqualTo(requests);
    }

    [Test]
    public async Task ChangingTheRouteParameterReRunsTheLookup()
    {
        var (cut, stub) = await Land(TestNupkg.Build(ownerId: "acme"));
        await cut.FindAll("button.mode-card")[0].ClickAsync();

        stub.Version = "2.0.0";
        cut.Render(_ => _.Add(_ => _.PackageId, "OtherPackage"));
        await cut.WaitForStateAsync(() => cut.Markup.Contains("Read from OtherPackage 2.0.0"));

        await Assert.That(cut.Markup).DoesNotContain("ThePackage 1.2.3");
        await Assert.That(stub.Requests.Any(_ => _.EndsWith("/otherpackage/index.json", StringComparison.Ordinal))).IsTrue();
        // a fresh model for the new package
        await Assert.That(cut.FindAll(".mode-card.selected").Count).IsEqualTo(0);

        // the same route value set again is not a new lookup
        var requests = stub.Requests.Count;
        cut.Render(_ => _.Add(_ => _.PackageId, "OtherPackage"));
        await Assert.That(stub.Requests.Count).IsEqualTo(requests);
    }

    [Test]
    public async Task RouteIdIsRequestedLowercasedAndShownVerbatim()
    {
        var (cut, stub) = await Land(TestNupkg.Build(), id: "My.Package");

        await Assert.That(cut.Markup).Contains("Read from My.Package 1.2.3");
        await Assert.That(stub.Requests[0]).EndsWith("/v3-flatcontainer/my.package/index.json");
    }

    [Test]
    public async Task StepperNavigatesBackToCompletedSteps()
    {
        var (cut, _) = await Land(TestNupkg.Build());
        // situation -> license mode
        await cut.Find("button.primary").ClickAsync();

        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".mode-cards").Count).IsEqualTo(1);

        await cut.Find(".stepper .step-link").ClickAsync();

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(0);

        await cut.Find("button.primary").ClickAsync();
        await cut.FindAll(".wizard-nav button").Single(_ => _.TextContent.Trim() == "Back").ClickAsync();

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);
    }

    /// <summary>Holds every request until released, so the loading state can be observed.</summary>
    sealed class GatedHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        readonly TaskCompletionSource gate = new();

        public void Release() => gate.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            await gate.Task;
            return await base.SendAsync(request, cancel);
        }
    }
}
