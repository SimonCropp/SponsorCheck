namespace SponsorCheck.Web.Tests.Components;

public class CodeBoxTests : WebTestContext
{
    [Test]
    public async Task RendersTitleAndContent()
    {
        var cut = Render<CodeBox>(_ => _
            .Add(_ => _.Title, "PackageReference")
            .Add(_ => _.Content, "<PackageReference Include=\"SponsorCheck\" />"));

        await Assert.That(cut.Find(".code-box-title").TextContent).IsEqualTo("PackageReference");
        await Assert.That(cut.Find("code").TextContent).Contains("SponsorCheck");
    }

    [Test]
    public async Task CopyButtonShowsCopiedAfterClick()
    {
        var cut = Render<CodeBox>(_ => _
            .Add(_ => _.Title, "Title")
            .Add(_ => _.Content, "hello"));

        await Assert.That(cut.Find(".copy-button").TextContent).IsEqualTo("Copy");

        cut.Find(".copy-button").Click();

        await Assert.That(cut.Find(".copy-button").TextContent).IsEqualTo("Copied!");
    }
}
