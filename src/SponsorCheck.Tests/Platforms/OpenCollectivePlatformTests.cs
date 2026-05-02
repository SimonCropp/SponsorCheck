public class OpenCollectivePlatformTests
{
    // Matches <UserSecretsId> in src/SponsorCheck/SponsorCheck.csproj.
    const string UserSecretsId = "0b81e813-4e7d-40f9-810b-9bd2cddd69e4";
    const string SecretKey = "SponsorCheck:OpenCollectiveToken";

    [Test]
    public async Task LiveLookup()
    {
        var secrets = UserSecretsReader.Read(UserSecretsId);
        if (!secrets.TryGetValue(SecretKey, out var token) || string.IsNullOrWhiteSpace(token))
        {
            Skip.Test($"User secret '{SecretKey}' not set under UserSecretsId '{UserSecretsId}'. Anonymous calls hit rate limits on collectives with many backers; create a Personal Token at https://opencollective.com/applications and run `dotnet user-secrets set {SecretKey} <pat>` in src/SponsorCheck.");
        }

        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new OpenCollectivePlatform();
        var sponsors = await platform.FetchSponsorAccounts("webpack", token, log, CancellationToken.None);
        await Assert.That(sponsors).IsNotNull();
    }

    [Test]
    public async Task PublicCollectiveBackers()
    {
        var json = """
        {
          "data": {
            "account": {
              "members": {
                "totalCount": 2,
                "nodes": [
                  { "account": { "slug": "alice" } },
                  { "account": { "slug": "acme-org" } }
                ]
              }
            }
          }
        }
        """;
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsTrue();
        await Assert.That(page.MemberSlugs).Contains("alice");
        await Assert.That(page.MemberSlugs).Contains("acme-org");
        await Assert.That(page.TotalCount).IsEqualTo(2);
    }

    [Test]
    public async Task UnknownAccount()
    {
        var json = """{ "data": { "account": null } }""";
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsFalse();
    }

    [Test]
    public void ErrorsThrow()
    {
        var json = """{ "errors": [ { "message": "boom" } ] }""";
        Assert.Throws<MaintenanceFeeException>(() => OpenCollectivePlatform.ParseResponse(json));
    }
}
