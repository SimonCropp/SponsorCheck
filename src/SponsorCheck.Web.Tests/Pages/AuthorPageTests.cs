namespace SponsorCheck.Web.Tests.Pages;

public class AuthorPageTests : WebTestContext
{
    [Test]
    public async Task NextDisabledUntilPackageIdEntered()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#packageId").InputAsync("MyOssLib");

        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task WalkthroughReachesOutput()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await cut.Find("#packageId").InputAsync("MyOssLib");
        // package -> platforms
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#enable-GitHub"), true);
        await cut.Find("#account-GitHub").InputAsync("acmecorp");
        // platforms -> mode
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        // mode -> options
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        // options -> output
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await Assert.That(cut.Markup).Contains("Tell your consumers");
        await Assert.That(cut.FindAll(".code-box").Count).IsEqualTo(4);
        await Assert.That(cut.FindAll("button.copy-markdown").Count).IsEqualTo(1);
    }

    [Test]
    public async Task StepperNavigatesBackToCompletedSteps()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(0);

        await cut.Find("#packageId").InputAsync("MyOssLib");
        // package -> platforms
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#enable-GitHub"), true);
        await cut.Find("#account-GitHub").InputAsync("acmecorp");
        // platforms -> mode
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(2);

        await EventHandlerDispatchExtensions.ClickAsync(cut.FindAll(".stepper .step-link")[0]);

        await Assert.That(cut.FindAll("#packageId").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".stepper .step-link").Count).IsEqualTo(0);
    }

    [Test]
    public async Task MonorepoShapePreselectsOwnerMode()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await cut.Find("#packageId").InputAsync("MyOssLib");
        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#shape-monorepo"), true);
        // package -> platforms
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#enable-GitHub"), true);
        await cut.Find("#account-GitHub").InputAsync("acmecorp");
        // platforms -> mode
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await Assert.That(cut.Find("#ownerMode").HasAttribute("checked")).IsTrue();
    }

    [Test]
    public async Task InvalidOwnerIdBlocksAdvance()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await cut.Find("#packageId").InputAsync("MyOssLib");
        // package -> platforms
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#enable-GitHub"), true);
        await cut.Find("#account-GitHub").InputAsync("acmecorp");
        // platforms -> mode
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#ownerMode"), true);
        await cut.Find("#ownerId").InputAsync("acme-corp");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("#ownerId").InputAsync("acme_corp");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task ExemptionRowsValidateLikeSc106()
    {
        var cut = Render<SponsorCheck.Web.Pages.Author>();

        await cut.Find("#packageId").InputAsync("MyOssLib");
        // package -> platforms
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#enable-GitHub"), true);
        await cut.Find("#account-GitHub").InputAsync("acmecorp");
        // platforms -> mode
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));
        // mode -> options
        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.primary"));

        await EventHandlerDispatchExtensions.ClickAsync(cut.Find("button.add-exemption"));
        await cut.Find("input.exemption-name").InputAsync("Consulting");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsTrue();

        await cut.Find("input.exemption-message").InputAsync("Consulting clients are exempt for 6 months.");

        await Assert.That(cut.FindAll(".validation-error").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }
}
