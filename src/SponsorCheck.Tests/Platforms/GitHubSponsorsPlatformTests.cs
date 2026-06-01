using System.Net;

public class GitHubSponsorsPlatformTests
{
    [Test]
    public async Task LiveLookup()
    {
        var token = LiveTokenResolver.ResolveOrSkip("GitHubToken", "SponsorCheck:GitHubToken", "GitHub Sponsors");
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new GitHubSponsorsPlatform();
        var sponsors = await platform.FetchSponsorAccounts("SimonCropp", token, log, Cancel.None);
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
        var json = """
                   {
                     "data": {
                       "user": null,
                       "organization": null
                     }
                   }
                   """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserExists).IsFalse();
        await Assert.That(page.OrgExists).IsFalse();
    }

    [Test]
    public void GraphQLErrorsThrow()
    {
        var json = """
                   {
                     "errors": [
                       { "message": "Bad credentials" }
                     ]
                   }
                   """;
        Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
    }

    [Test]
    public async Task NotFoundOnOrganizationPathIsIgnoredWhenUserResolves()
    {
        var json = """
        {
          "data": {
            "user": {
              "sponsors": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [ { "__typename": "User", "login": "alice" } ]
              }
            },
            "organization": null
          },
          "errors": [
            {
              "type": "NOT_FOUND",
              "path": [ "organization" ],
              "locations": [ { "line": 8, "column": 3 } ],
              "message": "Could not resolve to an Organization with the login of 'someuser'."
            }
          ]
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserExists).IsTrue();
        await Assert.That(page.OrgExists).IsFalse();
        await Assert.That(page.UserLogins).Contains("alice");
    }

    [Test]
    public async Task NotFoundOnUserPathIsIgnoredWhenOrganizationResolves()
    {
        var json = """
        {
          "data": {
            "user": null,
            "organization": {
              "sponsors": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [ { "__typename": "User", "login": "bob" } ]
              }
            }
          },
          "errors": [
            {
              "type": "NOT_FOUND",
              "path": [ "user" ],
              "message": "Could not resolve to a User with the login of 'someorg'."
            }
          ]
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.OrgExists).IsTrue();
        await Assert.That(page.UserExists).IsFalse();
        await Assert.That(page.OrgLogins).Contains("bob");
    }

    [Test]
    public void NonNotFoundErrorStillThrows()
    {
        var json = """
        {
          "errors": [
            { "type": "NOT_FOUND", "path": [ "organization" ], "message": "..." },
            { "type": "FORBIDDEN", "path": [ "user" ], "message": "Resource not accessible by integration" }
          ]
        }
        """;
        Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
    }

    [Test]
    public async Task ClassicPatForbiddenSurfacesActionableError()
    {
        var json = """
        {
          "errors": [
            {
              "type": "FORBIDDEN",
              "path": [ "organization" ],
              "extensions": { "saml_failure": false },
              "locations": [ { "line": 8, "column": 3 } ],
              "message": "`VerifyTests` forbids access via a personal access token (classic). Please use a GitHub App, OAuth App, or a personal access token with fine-grained permissions."
            }
          ]
        }
        """;
        var ex = Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
        await Assert.That(ex.Message).Contains("VerifyTests");
        await Assert.That(ex.Message).Contains("classic PAT");
        await Assert.That(ex.Message).Contains("re-enable classic-PAT access");
    }

    [Test]
    public void NotFoundOnUnrelatedPathStillThrows()
    {
        var json = """
        {
          "errors": [
            { "type": "NOT_FOUND", "path": [ "viewer" ], "message": "..." }
          ]
        }
        """;
        Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
    }

    [Test]
    public async Task MissingTokenThrowsTypedMissingCredentialException()
    {
        // GitHub's mandatory token check throws MissingCredentialException specifically (not the
        // base MaintenanceFeeException), so the bundler maps it to SC102. Unauthenticated GitHub
        // API calls hit a low rate limit and cause SC100 failures on shared CI IPs.
        var platform = new GitHubSponsorsPlatform();
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        MissingCredentialException? caught = null;
        try
        {
            await platform.FetchSponsorAccounts("acmecorp", token: null, log, Cancel.None);
        }
        catch (MissingCredentialException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("GitHub Sponsors");
    }

    [Test]
    public async Task FetchSponsorAccounts_ToleratesNotFoundOnOrganizationPath()
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
          },
          "errors": [
            { "type": "NOT_FOUND", "path": [ "organization" ], "message": "..." }
          ]
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var sponsors = await platform.FetchSponsorAccounts("alice-the-user", token: "fake", log, Cancel.None);
        await Assert.That(sponsors).Contains("alice");
        await Assert.That(sponsors).Contains("acmecorp");
    }

    sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
