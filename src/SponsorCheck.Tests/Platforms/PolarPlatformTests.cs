namespace SponsorCheck.Tests.Platforms;

using SponsorCheck.Tasks.Platforms;

public class PolarPlatformTests
{
    [Test]
    public async Task PrefersGithubUsername()
    {
        var json = """
        {
          "items": [
            { "user": { "github_username": "alice", "email": "alice@example.com" } },
            { "customer": { "github_username": "bob" } }
          ]
        }
        """;
        var page = PolarPlatform.ParseResponse(json);
        await Assert.That(page.SponsorAccounts).Contains("alice");
        await Assert.That(page.SponsorAccounts).Contains("bob");
    }

    [Test]
    public async Task FallsBackToEmail()
    {
        var json = """
        {
          "items": [
            { "user": { "email": "carol@example.com" } }
          ]
        }
        """;
        var page = PolarPlatform.ParseResponse(json);
        await Assert.That(page.SponsorAccounts).Contains("carol@example.com");
    }

    [Test]
    public async Task EmptyItems()
    {
        var json = """{ "items": [] }""";
        var page = PolarPlatform.ParseResponse(json);
        await Assert.That(page.SponsorAccounts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MissingItemsKey()
    {
        var json = """{ "totalCount": 0 }""";
        var page = PolarPlatform.ParseResponse(json);
        await Assert.That(page.SponsorAccounts.Count).IsEqualTo(0);
    }
}
