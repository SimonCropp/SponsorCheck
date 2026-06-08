using System.Net;

public class GitHubSponsorsPlatformTests
{
    [Test]
    public async Task LiveLookup()
    {
        var tokens = LiveTokenResolver.ResolveAllOrSkip("GitHubToken", "SponsorCheck:GitHubToken", "GitHub Sponsors");
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new GitHubSponsorsPlatform();
        var sponsors = await LivePlatformFetcher.FetchWithCandidateTokens(platform, "SimonCropp", tokens, log);
        await Assert.That(sponsors).IsNotNull();
    }


    [Test]
    public async Task UserSponsors()
    {
        var json = """
        {
          "data": {
            "user": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-01-15T10:00:00Z",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  },
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-02-15T10:00:00Z",
                    "sponsorEntity": { "__typename": "Organization", "login": "acmecorp" }
                  }
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
        await Assert.That(page.UserSponsorships.Select(_ => _.Login)).Contains("alice");
        await Assert.That(page.UserSponsorships.Select(_ => _.Login)).Contains("acmecorp");
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
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": true, "endCursor": "abc" },
                "nodes": [
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-03-01T00:00:00Z",
                    "sponsorEntity": { "__typename": "User", "login": "bob" }
                  }
                ]
              }
            }
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.OrgExists).IsTrue();
        await Assert.That(page.UserExists).IsFalse();
        await Assert.That(page.OrgSponsorships.Select(_ => _.Login)).Contains("bob");
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
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-01-15T10:00:00Z",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  }
                ]
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
        await Assert.That(page.UserSponsorships.Select(_ => _.Login)).Contains("alice");
    }

    [Test]
    public async Task NotFoundOnUserPathIsIgnoredWhenOrganizationResolves()
    {
        var json = """
        {
          "data": {
            "user": null,
            "organization": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-01-15T10:00:00Z",
                    "sponsorEntity": { "__typename": "User", "login": "bob" }
                  }
                ]
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
        await Assert.That(page.OrgSponsorships.Select(_ => _.Login)).Contains("bob");
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
    public async Task InsufficientScopesSurfacesActionableError()
    {
        // sponsorshipsAsMaintainer's per-sponsorship fields (isActive, isOneTimePayment, createdAt)
        // require read:user even when the maintainer is an organization. A token with only read:org
        // returns INSUFFICIENT_SCOPES and the bundler should explain how to fix it.
        var json = """
        {
          "errors": [
            {
              "type": "INSUFFICIENT_SCOPES",
              "message": "Your token has not been granted the required scopes to execute this query. The 'isActive' field requires one of the following scopes: ['read:user'], but your token has only been granted the: ['read:org'] scopes."
            }
          ]
        }
        """;
        var ex = Assert.Throws<MaintenanceFeeException>(() => GitHubSponsorsPlatform.ParseResponse(json));
        await Assert.That(ex.Message).Contains("read:user");
        await Assert.That(ex.Message).Contains("sponsorshipsAsMaintainer");
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
        var json = $$"""
        {
          "data": {
            "user": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  },
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
                    "sponsorEntity": { "__typename": "Organization", "login": "acmecorp" }
                  }
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

    [Test]
    public async Task IsValidAt_RecurringActive_Included()
    {
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "alice",
            IsOneTimePayment: false,
            IsActive: true,
            CreatedAt: new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
    }

    [Test]
    public async Task IsValidAt_RecurringInactive_Excluded()
    {
        // A cancelled recurring sponsor has isActive=false. activeOnly:false on the GraphQL side
        // surfaces them; the verifier must explicitly drop them so a lapsed sponsor doesn't ship
        // in the bundled hash list.
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "alice",
            IsOneTimePayment: false,
            IsActive: false,
            CreatedAt: new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task IsValidAt_OneTime_WithinOneMonth_Included()
    {
        // One-time sponsors are honoured for one month from createdAt — pairs with the OSS author
        // setting "Set minimum amount" on the GitHub Sponsors tier so a one-time payment of at least
        // the min monthly tier value buys an effective month of sponsor status.
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
    }

    [Test]
    public async Task IsValidAt_OneTime_OlderThanOneMonth_Excluded()
    {
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task IsValidAt_OneTime_AtExactlyOneMonth_Included()
    {
        // Boundary: createdAt + 30 days == now → still in. Excluding the boundary would silently
        // drop a sponsor on the very last day of their effective month.
        var createdAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: createdAt);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, createdAt + GitHubSponsorsPlatform.OneTimeWindow)).IsTrue();
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
