namespace SponsorCheck.Web.Tests.Pages;

public class ConsumerPageTests : WebTestContext
{
    [Test]
    public async Task CodeEntryPreAnswersOwnerMode()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("#scCode").Input("SC021");

        await Assert.That(cut.Find("#style-owner").HasAttribute("checked")).IsTrue();
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CodeEntryPreAnswersCpm()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("#scCode").Input("SC002");

        await Assert.That(cut.Find("#cpm-yes").HasAttribute("checked")).IsTrue();
    }

    [Test]
    public async Task AuthorCodeShowsRedirect()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("#scCode").Input("SC102");

        await Assert.That(cut.Find(".callout").TextContent).Contains("author flow");
    }

    [Test]
    public async Task UnrecognizedCodeShowsError()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("#scCode").Input("SC999");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
    }

    [Test]
    public async Task OwnerModeHidesCpmQuestion()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(1);

        cut.Find("#style-owner").Change(true);

        await Assert.That(cut.FindAll("#cpm-yes").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#ownerId").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WalkthroughSponsorNonCpmReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("button.primary").Click();           // situation -> package
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.primary").Click();           // package -> license mode

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

        cut.Find("#style-owner").Change(true);
        cut.Find("#ownerId").Input("acme");
        cut.Find("button.primary").Click();           // situation -> package
        cut.Find("button.primary").Click();           // package -> license mode (id optional in owner mode)

        cut.FindAll("button.mode-card")[1].Click();   // private license
        cut.Find("#licensedUntil").Input("2027-06");
        cut.Find("button.primary").Click();           // license mode -> output

        await Assert.That(cut.Markup).Contains("acme_SponsorshipLicensedUntil");
    }

    [Test]
    public async Task SponsorModeRequiresAccount()
    {
        var cut = Render<SponsorCheck.Web.Pages.Consumer>();

        cut.Find("button.primary").Click();           // situation -> package
        cut.Find("#packageId").Input("ThePackage");
        cut.Find("button.primary").Click();           // package -> license mode

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.FindAll("button.mode-card")[0].Click();   // sponsor

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("#sponsor-GitHub").Change(true);
        cut.Find("#sponsor-account-GitHub").Input("alice");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }
}
