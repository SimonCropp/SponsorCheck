namespace EnforceOssSponsorship.Tests.Platforms;

using EnforceOssSponsorship.Tasks.Platforms;

public class GitHubSponsorsPlatformTests
{
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
