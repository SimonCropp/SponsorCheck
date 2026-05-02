public class PolarPlatformTests
{
    // Matches <UserSecretsId> in src/SponsorCheck/SponsorCheck.csproj.
    const string userSecretsId = "0b81e813-4e7d-40f9-810b-9bd2cddd69e4";
    const string secretKey = "SponsorCheck:PolarToken";

    [Test]
    public async Task LiveLookup()
    {
        var secrets = UserSecretsReader.Read(userSecretsId);
        if (!secrets.TryGetValue(secretKey, out var token) || string.IsNullOrWhiteSpace(token))
        {
            Skip.Test($"User secret '{secretKey}' not set under UserSecretsId '{userSecretsId}'. Run `dotnet user-secrets set {secretKey} <pat>` in src/SponsorCheck.");
        }

        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new PolarPlatform();
        var sponsors = await platform.FetchSponsorAccounts("simoncropp", token, log, Cancel.None);
        await Assert.That(sponsors).IsNotNull();
    }

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
