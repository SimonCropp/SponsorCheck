namespace SponsorCheck.Web.Tests;

/// <summary>
/// One Verify.Playwright snapshot (full-page PNG + HTML) per wizard screen, driven through the real
/// WASM runtime. The wizard bundles its own fonts, so layout is identical on every OS and the PNG
/// baselines hold cross-platform; SSIM with a lenient threshold (see ModuleInitializer) absorbs the
/// remaining rasterization differences. The HTML target pins exact markup.
/// Inputs are fixed values so every screen renders deterministic content.
/// </summary>
public class ScreenSnapshotTests
{
    static PublishedWizard wizard => PublishedWizard.Shared;

    static async Task<IPage> Open(string path, string readySelector)
    {
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url(path));
        await page.WaitForSelectorAsync(readySelector);
        return page;
    }

    /// <summary>
    /// The bundled webfonts are declared <c>font-display: block</c>, so text stays unpainted until
    /// they load and a screenshot taken too early captures a blank or differently-measured page.
    /// </summary>
    static async Task VerifyScreen(IPage page)
    {
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        await Verify(page);
    }

    [Test]
    public async Task Home()
    {
        var page = await Open("/", ".role-cards");
        await VerifyScreen(page);
    }

    // ---- consumer screens ----

    static async Task<IPage> ConsumerPackageScreen()
    {
        var page = await Open("/consumer", "#packageId");
        await page.FillAsync("#packageId", "ThePackage");
        await page.FillAsync("#packageVersion", "1.2.3");
        return page;
    }

    static async Task<IPage> ConsumerSituationScreen()
    {
        var page = await ConsumerPackageScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync("#scCode");
        await page.FillAsync("#scCode", "SC001");
        await page.WaitForSelectorAsync("text=Recognized");
        return page;
    }

    static async Task<IPage> ConsumerLicenseModeScreen()
    {
        var page = await ConsumerSituationScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".mode-cards");
        // sponsor
        await page.ClickAsync("button.mode-card >> nth=0");
        await page.CheckAsync("#sponsor-GitHub");
        await page.FillAsync("#sponsor-account-GitHub", "alice");
        await page.WaitForSelectorAsync("#startedAfter");
        return page;
    }

    [Test]
    public async Task ConsumerPackage()
    {
        var page = await ConsumerPackageScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task ConsumerSituation()
    {
        var page = await ConsumerSituationScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task ConsumerLicenseMode()
    {
        var page = await ConsumerLicenseModeScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task ConsumerOutput()
    {
        var page = await ConsumerLicenseModeScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".output");
        await VerifyScreen(page);
    }

    // ---- author screens ----

    static async Task<IPage> AuthorPackageScreen()
    {
        var page = await Open("/author", "#packageId");
        await page.FillAsync("#packageId", "MyOssLib");
        await page.FillAsync("#packageVersion", "2.0.0");
        await page.FillAsync("#scVersion", "9.9.9");
        return page;
    }

    static async Task<IPage> AuthorPlatformsScreen()
    {
        var page = await AuthorPackageScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync("#enable-GitHub");
        await page.CheckAsync("#enable-GitHub");
        await page.FillAsync("#account-GitHub", "acmecorp");
        return page;
    }

    static async Task<IPage> AuthorModeScopeScreen()
    {
        var page = await AuthorPlatformsScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync("#ownerMode");
        await page.CheckAsync("#ownerMode");
        await page.WaitForSelectorAsync("#ownerId");
        await page.FillAsync("#ownerId", "acme");
        return page;
    }

    static async Task<IPage> AuthorOptionsScreen()
    {
        var page = await AuthorModeScopeScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".override-row");
        await page.ClickAsync("button.add-exemption");
        await page.FillAsync("input.exemption-name", "Consulting");
        await page.FillAsync("input.exemption-message", "Consulting clients are exempt for 6 months.");
        return page;
    }

    [Test]
    public async Task AuthorPackage()
    {
        var page = await AuthorPackageScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task AuthorPlatforms()
    {
        var page = await AuthorPlatformsScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task AuthorModeScope()
    {
        var page = await AuthorModeScopeScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task AuthorOptions()
    {
        var page = await AuthorOptionsScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task AuthorOutput()
    {
        var page = await AuthorOptionsScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".output");
        await VerifyScreen(page);
    }

    // ---- package deep-link screens ----

    /// <summary>The package page fetches on landing, so the fake feed is routed before navigating.</summary>
    static async Task<IPage> OpenPackage(byte[] nupkg, string readySelector, string id = "ThePackage")
    {
        var page = await wizard.NewPage();
        await FakeNuGetFeed.Route(page, nupkg, "1.2.3");
        await page.GotoAsync(wizard.Url($"/package/{id}"));
        await page.WaitForSelectorAsync(readySelector);
        return page;
    }

    static Task<IPage> PackageSituationScreen() =>
        OpenPackage(TestNupkg.Build(), "#cpm-yes");

    static async Task<IPage> PackageLicenseModeScreen()
    {
        // The bundled hash for the entered account makes this the one screen that captures the green
        // "found in the bundled list" verdict; the consumer screens never look anything up.
        var nupkg = TestNupkg.Build(
            ownerId: "acme",
            sponsorHashes: [SponsorAccountHash.For("GitHubSponsors", "alice")]);
        var page = await OpenPackage(nupkg, ".mode-cards");
        // sponsor
        await page.ClickAsync("button.mode-card >> nth=0");
        await page.CheckAsync("#sponsor-GitHub");
        await page.FillAsync("#sponsor-account-GitHub", "alice");
        await page.WaitForSelectorAsync(".match-confirmed");
        return page;
    }

    [Test]
    public async Task PackageSituation()
    {
        var page = await PackageSituationScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task PackageLicenseMode()
    {
        var page = await PackageLicenseModeScreen();
        await VerifyScreen(page);
    }

    [Test]
    public async Task PackageOutput()
    {
        var page = await PackageLicenseModeScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".output");
        await VerifyScreen(page);
    }

    [Test]
    public async Task PackageNotFound()
    {
        var page = await wizard.NewPage();
        await FakeNuGetFeed.RouteNotFound(page);
        await page.GotoAsync(wizard.Url("/package/NoSuchPackage"));
        await page.WaitForSelectorAsync("text=not found on nuget.org");
        await VerifyScreen(page);
    }
}
