namespace SponsorCheck.Web.Tests.Components;

public class AuthorOutputPanelTests : WebTestContext
{
    static AuthorModel FullModel()
    {
        var model = new AuthorModel { PackageId = "MyOssLib", PackageVersion = "2.0.0", SponsorCheckVersion = "9.9.9" };
        var gitHub = model.Selection(PlatformKind.GitHub);
        gitHub.Enabled = true;
        gitHub.Account = "acmecorp";
        return model;
    }

    [Test]
    public async Task RendersFourCodeBoxes()
    {
        var cut = Render<AuthorOutputPanel>(parameters => parameters.Add(_ => _.Model, FullModel()));
        await Assert.That(cut.FindAll(".code-box").Count).IsEqualTo(4);
    }

    [Test]
    public async Task IncludesAllParts()
    {
        var cut = Render<AuthorOutputPanel>(parameters => parameters.Add(_ => _.Model, FullModel()));
        var markup = cut.Markup;
        await Assert.That(markup).Contains("Reference SponsorCheck");
        await Assert.That(markup).Contains("Tell your consumers");
        await Assert.That(markup).Contains("Sponsorship is now checked at build time");
        await Assert.That(markup).Contains("Next steps");
    }

    [Test]
    public async Task CopyMarkdownSendsComposedDocument()
    {
        var cut = Render<AuthorOutputPanel>(parameters => parameters.Add(_ => _.Model, FullModel()));

        cut.Find("button.copy-markdown").Click();

        var invocation = JSInterop.Invocations.Single(_ => _.Identifier == "sponsorCheck.copyToClipboard");
        var copied = (string) invocation.Arguments[0]!;
        await Assert.That(copied).StartsWith("# Add SponsorCheck to MyOssLib");
        await Assert.That(copied).Contains("## 3. Release notes for consumers");
        await Assert.That(cut.Find("button.copy-markdown").TextContent).IsEqualTo("Copied!");
    }

    [Test]
    public async Task Markup()
    {
        var cut = Render<AuthorOutputPanel>(parameters => parameters.Add(_ => _.Model, FullModel()));
        await Verify(cut.Markup);
    }
}
