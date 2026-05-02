namespace SponsorCheck.Tests.Platforms;

using System.Threading;
using SponsorCheck.Tasks.Platforms;

public class GitHubSponsorsPlatformTests
{
    // Matches <UserSecretsId> in src/SponsorCheck/SponsorCheck.csproj.
    const string UserSecretsId = "0b81e813-4e7d-40f9-810b-9bd2cddd69e4";
    const string SecretKey = "SponsorCheck:GitHubToken";

    [Test]
    public async Task LiveLookup()
    {
        var secrets = UserSecretsReader.Read(UserSecretsId);
        if (!secrets.TryGetValue(SecretKey, out var token) || string.IsNullOrWhiteSpace(token))
        {
            Skip.Test($"User secret '{SecretKey}' not set under UserSecretsId '{UserSecretsId}'. Run `dotnet user-secrets set {SecretKey} <pat>` in src/SponsorCheck.");
        }

        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new GitHubSponsorsPlatform();
        var sponsors = await platform.FetchSponsorAccounts("SimonCropp", token, log, CancellationToken.None);
        await Assert.That(sponsors).IsNotNull();
    }


    [Test]
    public async Task UserSponsors()
    {
        var json = """
        {
          "data": {
            "user": {
              "sponsors": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  { "__typename": "User", "login": "alice" },
                  { "__typename": "Organization", "login": "acmecorp" }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserExists).IsTrue();
        await Assert.That(page.OrgExists).IsFalse();
        await Assert.That(page.UserLogins).Contains("alice");
        await Assert.That(page.UserLogins).Contains("acmecorp");
        await Assert.That(page.UserHasNextPage).IsFalse();
    }

    [Test]
    public async Task OrgSponsors()
    {
        var json = """
        {
          "data": {
            "user": null,
            "organization": {
              "sponsors": {
                "pageInfo": { "hasNextPage": true, "endCursor": "abc" },
                "nodes": [ { "__typename": "User", "login": "bob" } ]
              }
            }
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.OrgExists).IsTrue();
        await Assert.That(page.UserExists).IsFalse();
        await Assert.That(page.OrgLogins).Contains("bob");
        await Assert.That(page.OrgHasNextPage).IsTrue();
        await Assert.That(page.OrgEndCursor).IsEqualTo("abc");
    }

    [Test]
    public async Task NeitherExists()
    {
        var json = """{ "data": { "user": null, "organization": null } }""";
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserExists).IsFalse();
        await Assert.That(page.OrgExists).IsFalse();
    }

    [Test]
    public void GraphQLErrorsThrow()
    {
        var json = """{ "errors": [ { "message": "Bad credentials" } ] }""";
        Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
    }
}
