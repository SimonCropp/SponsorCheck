namespace SponsorCheck.Web.Tests.Pages;

public class AuthorPageTests : WebTestContext
{
    [Test]
    public async Task NextDisabledUntilPackageIdEntered()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("#packageId").Input("MyOssLib");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task WalkthroughReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        cut.Find("#packageId").Input("MyOssLib");
        cut.Find("button.primary").Click();           // package -> platforms

        cut.Find("#enable-GitHub").Change(true);
        cut.Find("#account-GitHub").Input("acmecorp");
        cut.Find("button.primary").Click();           // platforms -> mode
        cut.Find("button.primary").Click();           // mode -> options
        cut.Find("button.primary").Click();           // options -> output

        await Assert.That(cut.Markup).Contains("Tell your consumers");
        await Assert.That(cut.FindAll(".code-box").Count).IsEqualTo(4);
        await Assert.That(cut.FindAll("button.copy-markdown").Count).IsEqualTo(1);
    }

    [Test]
    public async Task MonorepoShapePreselectsOwnerMode()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        cut.Find("#packageId").Input("MyOssLib");
        cut.Find("#shape-monorepo").Change(true);
        cut.Find("button.primary").Click();           // package -> platforms

        cut.Find("#enable-GitHub").Change(true);
        cut.Find("#account-GitHub").Input("acmecorp");
        cut.Find("button.primary").Click();           // platforms -> mode

        await Assert.That(cut.Find("#ownerMode").HasAttribute("checked")).IsTrue();
    }

    [Test]
    public async Task InvalidOwnerIdBlocksAdvance()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        cut.Find("#packageId").Input("MyOssLib");
        cut.Find("button.primary").Click();           // package -> platforms
        cut.Find("#enable-GitHub").Change(true);
        cut.Find("#account-GitHub").Input("acmecorp");
        cut.Find("button.primary").Click();           // platforms -> mode

        cut.Find("#ownerMode").Change(true);
        cut.Find("#ownerId").Input("acme-corp");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("#ownerId").Input("acme_corp");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task ExemptionRowsValidateLikeSc106()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        cut.Find("#packageId").Input("MyOssLib");
        cut.Find("button.primary").Click();           // package -> platforms
        cut.Find("#enable-GitHub").Change(true);
        cut.Find("#account-GitHub").Input("acmecorp");
        cut.Find("button.primary").Click();           // platforms -> mode
        cut.Find("button.primary").Click();           // mode -> options

        cut.Find("button.add-exemption").Click();
        cut.Find("input.exemption-name").Input("Consulting");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        cut.Find("input.exemption-message").Input("Consulting clients are exempt for 6 months.");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }
}
