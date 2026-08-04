namespace SponsorCheck.Web.Tests;

/// <summary>
/// Behavioural end-to-end journeys over the published app (see <see cref="PublishedWizard"/>).
/// Text/selector assertions only — per-screen visual coverage lives in <see cref="ScreenSnapshotTests"/>.
/// </summary>
public class EndToEndTests
{
    static PublishedWizard wizard => PublishedWizard.Shared;

    [Test]
    public async Task Boots()
    {
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url());
        await page.WaitForSelectorAsync(".role-cards");

        var cards = await page.QuerySelectorAllAsync("a.role-card");
        await Assert.That(cards.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AuthorJourney()
    {
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url());
        await page.ClickAsync("a.role-card[href='author']");
        await page.WaitForSelectorAsync("#packageId");

        await page.FillAsync("#packageId", "MyOssLib");
        // package -> platforms
        await page.ClickAsync("button.primary");
        await page.CheckAsync("#enable-GitHub");
        await page.FillAsync("#account-GitHub", "acmecorp");
        // platforms -> mode
        await page.ClickAsync("button.primary");
        // mode -> options
        await page.ClickAsync("button.primary");
        // options -> output
        await page.ClickAsync("button.primary");

        await page.WaitForSelectorAsync(".code-box");
        var body = await page.TextContentAsync("body");

        await Assert.That(body).Contains("GitHubSponsorsAccount=\"acmecorp\"");
        await Assert.That(body).Contains("Tell your consumers");
        await Assert.That(body).Contains("Copy as markdown");
    }

    [Test]
    public async Task ConsumerOwnerJourney()
    {
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url());
        await page.ClickAsync("a.role-card[href='consumer']");
        await page.WaitForSelectorAsync("#packageId");

        await page.FillAsync("#packageId", "ThePackage");
        // package -> situation (no lookup: offline path)
        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync("#scCode");
        await page.FillAsync("#scCode", "SC021");
        await page.WaitForSelectorAsync("#ownerId");
        await page.FillAsync("#ownerId", "acme");
        // situation -> license mode
        await page.ClickAsync("button.primary");

        // sponsor
        await page.ClickAsync("button.mode-card >> nth=0");
        await page.CheckAsync("#sponsor-GitHub");
        await page.FillAsync("#sponsor-account-GitHub", "alice");
        // license mode -> output
        await page.ClickAsync("button.primary");

        await page.WaitForSelectorAsync(".code-box");
        var body = await page.TextContentAsync("body");

        await Assert.That(body).Contains("<acme_GitHubSponsorAccount>alice</acme_GitHubSponsorAccount>");
        await Assert.That(body).Contains("Directory.Build.props");
    }

    [Test]
    public async Task DeepLinkToConsumerFlow()
    {
        // Exercises the SPA fallback (404.html / MapFallbackToFile) that GitHub Pages relies on.
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url("/consumer"));
        await page.WaitForSelectorAsync("#packageId");

        var heading = await page.TextContentAsync("h2");
        await Assert.That(heading).IsEqualTo("The package");
    }
}
