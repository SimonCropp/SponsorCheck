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
        await Assert.That(page.RawItemCount).IsEqualTo(0);
    }

    [Test]
    public async Task RawItemCount_CountsAllItemsIncludingUnusableOnes()
    {
        // Pagination must terminate on raw API page size, not parsed-account count. An item with
        // every fallback null gets dropped from SponsorAccounts but still consumes one of `limit`
        // rows on the page. If we used SponsorAccounts.Count, a sparse page would stop pagination
        // early and silently miss subsequent pages of sponsors.
        var json = """
        {
          "items": [
            { "user": { "github_username": "alice" } },
            { "user": { "github_username": null, "email": null }, "user_id": null, "customer": null },
            { "customer": { "github_username": "bob" } }
          ]
        }
        """;
        var page = PolarPlatform.ParseResponse(json);
        await Assert.That(page.SponsorAccounts.Count).IsEqualTo(2);
        await Assert.That(page.RawItemCount).IsEqualTo(3);
    }

    [Test]
    public async Task MissingTokenThrowsTypedMissingCredentialException()
    {
        // Polar's mandatory token check throws MissingCredentialException specifically (not the
        // base MaintenanceFeeException), so the bundler can map it to SC103.
        var platform = new PolarPlatform();
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        MissingCredentialException? caught = null;
        try
        {
            await platform.FetchSponsorAccounts("acme", token: null, log, Cancel.None);
        }
        catch (MissingCredentialException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("Polar");
        // The misleading "(SC103)" suffix in the message was removed when we made the diagnostic
        // structured (caught by typed exception, mapped to SC103 by the bundler).
        await Assert.That(caught.Message).DoesNotContain("(SC103)");
    }

    [Test]
    public async Task MissingCredentialExceptionInheritsFromMaintenanceFeeException()
    {
        // Existing catch sites that handle MaintenanceFeeException still work for the typed subclass.
        MaintenanceFeeException ex = new MissingCredentialException("test");
        await Assert.That(ex.Message).IsEqualTo("test");
    }
}
