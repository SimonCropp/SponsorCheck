namespace SponsorCheck.Web.Tests.Components;

public class ConsumerOutputPanelTests : WebTestContext
{
    static ConsumerModel SponsorModel()
    {
        var model = new ConsumerModel
        {
            PackageId = "ThePackage",
            PackageVersion = "1.2.3",
            Mode = ConsumerLicenseMode.Sponsor
        };
        var gitHub = model.Selection(PlatformKind.GitHub);
        gitHub.Enabled = true;
        gitHub.Account = "alice";
        return model;
    }

    [Test]
    public async Task IncludesAllParts()
    {
        var cut = Render<ConsumerOutputPanel>(_ => _.Add(_ => _.Model, SponsorModel()));
        var markup = cut.Markup;
        await Assert.That(markup).Contains("File to edit");
        await Assert.That(markup).Contains("Expected build outcome");
        await Assert.That(markup).Contains("GitHubSponsorAccount");
    }

    [Test]
    public async Task CopyMarkdownSendsComposedDocument()
    {
        var cut = Render<ConsumerOutputPanel>(_ => _.Add(_ => _.Model, SponsorModel()));

        await cut.Find("button.copy-markdown").ClickAsync();

        var invocation = JSInterop.Invocations.Single(_ => _.Identifier == "sponsorCheck.copyToClipboard");
        var copied = (string) invocation.Arguments[0]!;
        await Assert.That(copied).StartsWith("# Configure SponsorCheck licensing for ThePackage");
        await Assert.That(copied).Contains("## Expected build outcome");
    }

    [Test]
    public async Task Markup()
    {
        var cut = Render<ConsumerOutputPanel>(_ => _.Add(_ => _.Model, SponsorModel()));
        await Verify(cut.Markup);
    }

    [Test]
    public async Task CopyMarkdownLinksTheGivenWizardUrl()
    {
        var packageUrl = WizardLinks.Package("ThePackage");
        var cut = Render<ConsumerOutputPanel>(_ => _
            .Add(_ => _.Model, SponsorModel())
            .Add(_ => _.WizardUrl, packageUrl));

        await cut.Find("button.copy-markdown").ClickAsync();

        var invocation = JSInterop.Invocations.Single(_ => _.Identifier == "sponsorCheck.copyToClipboard");
        var copied = (string) invocation.Arguments[0]!;
        await Assert.That(copied).Contains($"({packageUrl})");
        await Assert.That(copied).DoesNotContain($"({WizardLinks.Consumer})");
    }
}
