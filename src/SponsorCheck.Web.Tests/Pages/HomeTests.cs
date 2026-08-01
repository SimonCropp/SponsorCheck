namespace SponsorCheck.Web.Tests.Pages;

public class HomeTests : WebTestContext
{
    [Test]
    public async Task RendersBothRoleCards()
    {
        var cut = Render<SponsorCheck.Web.Pages.Home>();

        var cards = cut.FindAll("a.role-card");
        await Assert.That(cards.Count).IsEqualTo(2);
        await Assert.That(cards[0].GetAttribute("href")).IsEqualTo("consumer");
        await Assert.That(cards[1].GetAttribute("href")).IsEqualTo("author");
    }
}
