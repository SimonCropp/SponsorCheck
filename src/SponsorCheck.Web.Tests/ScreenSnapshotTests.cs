namespace SponsorCheck.Web.Tests;

/// <summary>
/// One Verify.Playwright snapshot (full-page PNG + HTML) per wizard screen, driven through the real
/// WASM runtime. PNG comparison uses SSIM with a lenient threshold (see ModuleInitializer) so the
/// Windows-authored baselines tolerate Linux CI font rendering; the HTML target pins exact markup.
/// Inputs are fixed values so every screen renders deterministic content.
/// </summary>
public class ScreenSnapshotTests
{
    static PublishedWizard? wizard;

    [Before(Class)]
    public static async Task Setup() => wizard = await PublishedWizard.Start();

    [After(Class)]
    public static async Task Teardown()
    {
        if (wizard != null)
        {
            await wizard.DisposeAsync();
        }
    }

    static async Task<IPage> Open(string path, string readySelector)
    {
        var page = await wizard!.NewPage();
        await page.GotoAsync(wizard.Url(path));
        await page.WaitForSelectorAsync(readySelector);
        return page;
    }

    [Test]
    public async Task Home()
    {
        var page = await Open("/", ".role-cards");
        await Verify(page);
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
        await page.ClickAsync("button.mode-card >> nth=0");   // sponsor
        await page.CheckAsync("#sponsor-GitHub");
        await page.FillAsync("#sponsor-account-GitHub", "alice");
        await page.WaitForSelectorAsync("#startedAfter");
        return page;
    }

    [Test]
    public async Task ConsumerPackage()
    {
        var page = await ConsumerPackageScreen();
        await Verify(page);
    }

    [Test]
    public async Task ConsumerSituation()
    {
        var page = await ConsumerSituationScreen();
        await Verify(page);
    }

    [Test]
    public async Task ConsumerLicenseMode()
    {
        var page = await ConsumerLicenseModeScreen();
        await Verify(page);
    }

    [Test]
    public async Task ConsumerOutput()
    {
        var page = await ConsumerLicenseModeScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".output");
        await Verify(page);
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
        await Verify(page);
    }

    [Test]
    public async Task AuthorPlatforms()
    {
        var page = await AuthorPlatformsScreen();
        await Verify(page);
    }

    [Test]
    public async Task AuthorModeScope()
    {
        var page = await AuthorModeScopeScreen();
        await Verify(page);
    }

    [Test]
    public async Task AuthorOptions()
    {
        var page = await AuthorOptionsScreen();
        await Verify(page);
    }

    [Test]
    public async Task AuthorOutput()
    {
        var page = await AuthorOptionsScreen();
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".output");
        await Verify(page);
    }
}
