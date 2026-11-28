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
    public async Task FetchSponsorAccounts_SendsTheQueryAndTheLogin()
    {
        // The tests reference the Release build of the task assembly, which is the trimmed one.
        // When the request body was an anonymous type handed to JsonSerializer, the trimmer
        // removed its getters, the body went out as "{}", and GitHub answered that no query was
        // specified. Nothing that only reads a canned response notices that.
        var capture = new RequestCapturingHandler("""{ "data": { "user": null, "organization": null } }""");
        using var client = new HttpClient(capture);
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        try
        {
            await platform.FetchSponsorAccounts("alice-the-user", token: "fake", log, Cancel.None);
        }
        catch (MaintenanceFeeException)
        {
            // Neither a user nor an organization: the request is what is under test.
        }

        using var body = System.Text.Json.JsonDocument.Parse(capture.LastRequestBody!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        await Assert.That(query).Contains("sponsorshipsAsMaintainer");
        // What a one-time payment's window is sized and dated from.
        await Assert.That(query).Contains("tierSelectedAt");
        await Assert.That(query).Contains("sponsorsListing");
        var variables = body.RootElement.GetProperty("variables");
        await Assert.That(variables.GetProperty("login").GetString()).IsEqualTo("alice-the-user");
        await Assert.That(variables.GetProperty("cursor").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task IsValidAt_RecurringActive_Included()
    {
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "alice",
            IsOneTimePayment: false,
            IsActive: true,
            CreatedAt: new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate: false);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
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
            CreatedAt: new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate: false);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task IsValidAt_OneTime_WithinOneMonth_Included()
    {
        // With no rate to scale by, a one-time payment counts for one month from when it was paid,
        // which is also the least any one-time payment gets.
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate: false);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
    }

    [Test]
    public async Task IsValidAt_OneTime_OlderThanOneMonth_Excluded()
    {
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate: false);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task IsValidAt_OneTime_AtExactlyOneMonth_Included()
    {
        // Boundary: one calendar month after the payment == now → still in. Excluding the boundary
        // would silently drop a sponsor on the very last day of their effective month.
        var createdAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var entry = new GitHubSponsorsPlatform.SponsorshipEntry(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: createdAt,
            IsPrivate: false);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], createdAt.AddMonths(1))).IsTrue();
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [], createdAt.AddMonths(1).AddSeconds(1))).IsFalse();
    }

    // --- A one-time payment's window scales with its amount ---
    //
    // GitHub Sponsors has no yearly tier, so a sponsor paying for a year up front does it as a
    // one-time payment. A maintainer's one-time tier at twelve times a monthly tier is a year;
    // anything else counts for the months it would have bought at the lowest published monthly tier.

    static GitHubSponsorsPlatform.SponsorshipEntry OneTime(
        DateTime createdAt,
        int? priceInCents,
        DateTime? tierSelectedAt = null,
        bool onMaintainerOneTimeTier = false) =>
        new(
            Login: "carol",
            IsOneTimePayment: true,
            IsActive: false,
            CreatedAt: createdAt,
            IsPrivate: false,
            TierSelectedAt: tierSelectedAt,
            TierPriceInCents: priceInCents,
            OnMaintainerOneTimeTier: onMaintainerOneTimeTier);

    [Test]
    [Arguments(1000, 1000, 1)]
    [Arguments(2500, 1000, 2)]
    [Arguments(12000, 1000, 12)]
    [Arguments(12000, 100, 120)]
    [Arguments(500, 1000, 1)]
    public async Task PaidMonths_TheAmountOverTheLowestMonthlyTier_RoundedDown(int priceInCents, int lowestMonthlyTierInCents, int expected)
    {
        // The last case is a payment below the tier, which still gets the month it always did.
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), priceInCents);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, [lowestMonthlyTierInCents])).IsEqualTo(expected);
    }

    [Test]
    public async Task PaidMonths_NoPublishedMonthlyTier_IsOneMonth()
    {
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 12000);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, [])).IsEqualTo(1);
    }

    [Test]
    public async Task PaidMonths_NoTierOnTheSponsorship_IsOneMonth()
    {
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, [1000])).IsEqualTo(1);
    }

    [Test]
    public async Task IsValidAt_OneTime_YearPaidUpFront_LastsTheCalendarYear()
    {
        // $120 against a $10 tier. Twelve 30-day blocks would have ended on 2026-11-26, five days
        // short of the year that was paid for.
        var entry = OneTime(new(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), 12000);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1000], new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1000], new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1000], new(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task IsValidAt_OneTime_RepeatPayment_DatedByTheLatestPayment()
    {
        // A sponsor who pays again keeps the same sponsorship: createdAt stays at the first payment
        // and tierSelectedAt moves to the latest. Dated by createdAt, this sponsor would have been
        // dropped four years ago.
        var entry = OneTime(
            new(2022, 6, 24, 0, 0, 0, DateTimeKind.Utc),
            1000,
            tierSelectedAt: new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1000], new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1000], new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task PaidAt_TierSelectedJustBeforeCreation_IsTheCreationTime()
    {
        // GitHub records a first payment's tier a second before the sponsorship itself.
        var createdAt = new DateTime(2025, 11, 18, 20, 22, 58, DateTimeKind.Utc);
        var entry = OneTime(createdAt, 1000, tierSelectedAt: createdAt.AddSeconds(-1));
        await Assert.That(entry.PaidAt).IsEqualTo(createdAt);
    }

    [Test]
    public async Task IsValidAt_OneTime_OutsizedPayment_IsCappedRatherThanOverflowing()
    {
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), int.MaxValue);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, [1])).IsEqualTo(GitHubSponsorsPlatform.MaxPaidMonths);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, [1], new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
    }

    // --- Yearly tiers ---
    //
    // How an author with several monthly tiers offers yearly sponsorship: a one-time tier for each,
    // at twelve times its price. Verify's tiers are priced by the sponsor's size, so the amount alone
    // can't say which tier a payment was for; the tier the sponsor picked can.

    static readonly int[] verifyTiers = [500, 2000, 3000, 10000];

    [Test]
    [Arguments(6000)]
    [Arguments(24000)]
    [Arguments(36000)]
    [Arguments(120000)]
    public async Task PaidMonths_MaintainerOneTimeTierAtTwelveTimesAMonthlyTier_IsAYear(int priceInCents)
    {
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), priceInCents, onMaintainerOneTimeTier: true);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, verifyTiers)).IsEqualTo(12);
    }

    [Test]
    public async Task PaidMonths_CustomAmountAtTwelveTimesAMonthlyTier_KeepsTheLowestTierRule()
    {
        // The same $1,200 typed in at checkout says nothing about which tier it was for, so it is
        // priced at the cheapest one, as a recurring sponsor's tier effectively is.
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 120000);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, verifyTiers)).IsEqualTo(240);
    }

    [Test]
    [Arguments(100000, 200)]
    [Arguments(10000, 20)]
    public async Task PaidMonths_MaintainerOneTimeTierNotTwelveTimesAMonthlyTier_KeepsTheLowestTierRule(int priceInCents, int expected)
    {
        // A "two months free" yearly tier at $1,000, and a one-time tier at one month of the top
        // tier. Neither mirrors a monthly tier, so neither is recognised as a year.
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), priceInCents, onMaintainerOneTimeTier: true);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, verifyTiers)).IsEqualTo(expected);
    }

    [Test]
    public async Task PaidMonths_CoffeeTier_IsUnaffected()
    {
        // A $5 one-time tier beside a $10 monthly one is not a year of anything.
        var entry = OneTime(new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 500, onMaintainerOneTimeTier: true);
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(entry, [1000])).IsEqualTo(1);
    }

    [Test]
    public async Task PaidMonths_TheYearlyRuleNeverLengthensAWindow()
    {
        // What makes recognising yearly tiers safe for a listing whose one-time tiers mean something
        // else: a maintainer's tier never counts for longer than a custom amount of the same price.
        var paidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var lengthened = Enumerable.Range(1, 2000)
            .Select(_ => _ * 100)
            .Where(_ => GitHubSponsorsPlatform.PaidMonths(OneTime(paidAt, _, onMaintainerOneTimeTier: true), verifyTiers) >
                        GitHubSponsorsPlatform.PaidMonths(OneTime(paidAt, _), verifyTiers))
            .ToList();
        await Assert.That(lengthened).IsEmpty();
    }

    [Test]
    public async Task IsValidAt_YearlyTier_LastsTheYear()
    {
        // Verify's over-1000-employees tier paid for a year. Priced at the cheapest tier, the same
        // $1,200 would have counted for twenty years.
        var entry = OneTime(new(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), 120000, onMaintainerOneTimeTier: true);
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, verifyTiers, new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))).IsTrue();
        await Assert.That(GitHubSponsorsPlatform.IsValidAt(entry, verifyTiers, new(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc))).IsFalse();
    }

    [Test]
    public async Task ParseResponse_TellsAMaintainerOneTimeTierFromACustomAmount()
    {
        // ivan's tier has lost its flags. It is read as a custom amount, whose window is never the
        // shorter one, rather than as a yearly tier.
        var json = """
        {
          "data": {
            "user": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "2026-05-15T00:00:00Z",
                    "tier": { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "User", "login": "grace" }
                  },
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "2026-05-15T00:00:00Z",
                    "tier": { "isOneTime": true, "isCustomAmount": true, "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "User", "login": "henry" }
                  },
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "2026-05-15T00:00:00Z",
                    "tier": { "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "User", "login": "ivan" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserSponsorships.Single(_ => _.Login == "grace").OnMaintainerOneTimeTier).IsTrue();
        await Assert.That(page.UserSponsorships.Single(_ => _.Login == "henry").OnMaintainerOneTimeTier).IsFalse();
        await Assert.That(page.UserSponsorships.Single(_ => _.Login == "ivan").OnMaintainerOneTimeTier).IsFalse();
    }

    [Test]
    public async Task FetchSponsorAccounts_YearlyTier_CountsForTheYearAndNoLonger()
    {
        // Against Verify's tiers. grace paid the $1,200 yearly tier thirteen months ago, so her year
        // is over; henry typed in the same $1,200 then and is priced at the $5 tier; judy paid the
        // yearly tier six months ago.
        var thirteenMonthsAgo = DateTime.UtcNow.AddMonths(-13);
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var json = $$"""
        {
          "data": {
            "user": null,
            "organization": {
              "sponsorsListing": {
                "tiers": {
                  "nodes": [
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 500 },
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 2000 },
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 3000 },
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 10000 },
                    { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 120000 }
                  ]
                }
              },
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "{{thirteenMonthsAgo:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tier": { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "Organization", "login": "grace" }
                  },
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "{{thirteenMonthsAgo:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tier": { "isOneTime": true, "isCustomAmount": true, "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "Organization", "login": "henry" }
                  },
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "{{sixMonthsAgo:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tier": { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 120000 },
                    "sponsorEntity": { "__typename": "Organization", "login": "judy" }
                  }
                ]
              }
            }
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var sponsors = await platform.FetchSponsorAccounts("VerifyTests", token: "fake", log, Cancel.None);
        await Assert.That(sponsors).DoesNotContain("grace");
        await Assert.That(sponsors).Contains("henry");
        await Assert.That(sponsors).Contains("judy");
    }

    [Test]
    public async Task ParseResponse_ReadsTheTierPriceAndTierSelectedAt()
    {
        var json = """
        {
          "data": {
            "user": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "2022-06-24T17:49:55Z",
                    "tierSelectedAt": "2026-06-02T18:45:38Z",
                    "tier": { "monthlyPriceInCents": 12000 },
                    "sponsorEntity": { "__typename": "User", "login": "carol" }
                  },
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
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        var carol = page.UserSponsorships.Single(_ => _.Login == "carol");
        await Assert.That(carol.TierPriceInCents).IsEqualTo(12000);
        await Assert.That(carol.TierSelectedAt).IsEqualTo(new DateTime(2026, 6, 2, 18, 45, 38, DateTimeKind.Utc));
        await Assert.That(carol.PaidAt).IsEqualTo(new DateTime(2026, 6, 2, 18, 45, 38, DateTimeKind.Utc));
        // Neither field is required: an entry without them parses as it did before they were read.
        var alice = page.UserSponsorships.Single(_ => _.Login == "alice");
        await Assert.That(alice.TierPriceInCents).IsNull();
        await Assert.That(alice.TierSelectedAt).IsNull();
    }

    [Test]
    public async Task ParseResponse_MonthlyTiers_SkipOneTimeTiersAndCustomAmounts()
    {
        // The last tier has lost its isOneTime flag. It is skipped rather than read as monthly, so a
        // renamed flag falls back to the one-month window instead of letting a one-time tier set the
        // rate.
        var json = """
        {
          "data": {
            "user": null,
            "organization": {
              "sponsorsListing": {
                "tiers": {
                  "nodes": [
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 1000 },
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 300 },
                    { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 100 },
                    { "isOneTime": false, "isCustomAmount": true, "monthlyPriceInCents": 200 },
                    { "isCustomAmount": false, "monthlyPriceInCents": 50 }
                  ]
                }
              },
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": []
              }
            }
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.OrgMonthlyTiersInCents!).IsEquivalentTo([1000, 300]);
        await Assert.That(page.UserMonthlyTiersInCents!).IsEmpty();
    }

    [Test]
    public async Task ParseResponse_OnlyOneTimeTiers_LeavesTheRateUnknown()
    {
        var json = """
        {
          "data": {
            "user": {
              "sponsorsListing": {
                "tiers": {
                  "nodes": [
                    { "isOneTime": true, "isCustomAmount": false, "monthlyPriceInCents": 2500 }
                  ]
                }
              },
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": []
              }
            },
            "organization": null
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserMonthlyTiersInCents!).IsEmpty();
    }

    [Test]
    public async Task ParseResponse_RefusedWindowFields_FallBackInsteadOfFailing()
    {
        // The tier and the listing only lengthen a one-time payment's window. If GitHub refuses them
        // to the viewer, the pack has to go on with the one-month window rather than fail with SC100
        // for an author whose sponsor list read fine before these fields were asked for.
        var json = """
        {
          "data": {
            "user": {
              "sponsorsListing": null,
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "2026-05-15T00:00:00Z",
                    "tier": null,
                    "sponsorEntity": { "__typename": "User", "login": "carol" }
                  }
                ]
              }
            },
            "organization": null
          },
          "errors": [
            { "type": "FORBIDDEN", "path": [ "user", "sponsorshipsAsMaintainer", "nodes", 0, "tier" ], "message": "..." },
            { "type": "FORBIDDEN", "path": [ "user", "sponsorsListing" ], "message": "..." }
          ]
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        var carol = page.UserSponsorships.Single();
        await Assert.That(carol.TierPriceInCents).IsNull();
        await Assert.That(page.UserMonthlyTiersInCents!).IsEmpty();
        await Assert.That(GitHubSponsorsPlatform.PaidMonths(carol, page.UserMonthlyTiersInCents!)).IsEqualTo(1);
    }

    [Test]
    public async Task FetchSponsorAccounts_OneTimePayment_CountsForTheMonthsItPaidFor()
    {
        // Both paid six months ago against a $10 tier. erin's $120 is inside its year; frank's $10
        // ran out five months ago.
        var paidAt = DateTime.UtcNow.AddMonths(-6);
        var json = $$"""
        {
          "data": {
            "user": {
              "sponsorsListing": {
                "tiers": {
                  "nodes": [
                    { "isOneTime": false, "isCustomAmount": false, "monthlyPriceInCents": 1000 }
                  ]
                }
              },
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "{{paidAt:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tierSelectedAt": "{{paidAt:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tier": { "monthlyPriceInCents": 12000 },
                    "sponsorEntity": { "__typename": "User", "login": "erin" }
                  },
                  {
                    "isActive": false,
                    "isOneTimePayment": true,
                    "createdAt": "{{paidAt:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tierSelectedAt": "{{paidAt:yyyy-MM-ddTHH:mm:ssZ}}",
                    "tier": { "monthlyPriceInCents": 1000 },
                    "sponsorEntity": { "__typename": "User", "login": "frank" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var sponsors = await platform.FetchSponsorAccounts("acmecorp", token: "fake", log, Cancel.None);
        await Assert.That(sponsors).Contains("erin");
        await Assert.That(sponsors).DoesNotContain("frank");
    }

    [Test]
    public async Task Unauthorized_ThrowsInvalidCredentialRatherThanGenericPlatformError()
    {
        // A 401 used to surface as a raw "GitHub GraphQL HTTP 401: {...}" under the SC100 catch-all,
        // which reads like a transient platform fault and invites a pointless re-run. It is always a
        // dead stored credential, so it gets its own type (SC107) and never echoes the response body.
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        InvalidCredentialException? thrown = null;
        try
        {
            await platform.FetchSponsorAccounts("SimonCropp", $"ghp_{new string('x', 36)}", log, Cancel.None);
        }
        catch (InvalidCredentialException exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("HTTP 401");
        await Assert.That(thrown.Message).DoesNotContain("xxxx");
    }

    [Test]
    public async Task CredentialRejected_ClassicPat_PointsAtTheStoredValueNotPermissions()
    {
        var message = GitHubSponsorsPlatform.CredentialRejectedMessage($"ghp_{new string('x', 36)}");
        await Assert.That(message).Contains("ghp_…, 40 chars");
        await Assert.That(message).Contains("deleted or regenerated");
        // The failure this replaces sent the author hunting through SSO and org settings.
        await Assert.That(message).Contains("not a scope, SSO, or org-policy failure");
    }

    [Test]
    public async Task CredentialRejected_FineGrainedPat_SaysItCannotWorkAtAll()
    {
        // Fine-grained PATs expose no Sponsorships permission, so no amount of re-issuing helps —
        // the author has to switch token type, which a bare 401 gives no hint of.
        var message = GitHubSponsorsPlatform.CredentialRejectedMessage($"github_pat_{new string('x', 22)}_{new string('y', 59)}");
        await Assert.That(message).Contains("github_pat_…");
        await Assert.That(message).Contains("no Sponsorships permission");
    }

    [Test]
    public async Task CredentialRejected_ActionsToken_NamesTheActionsMistake()
    {
        var message = GitHubSponsorsPlatform.CredentialRejectedMessage($"ghs_{new string('x', 36)}");
        await Assert.That(message).Contains("secrets.GITHUB_TOKEN");
    }

    [Test]
    public async Task CredentialRejected_UnrecognizedValue_FlagsTruncationOrPlaceholder()
    {
        var message = GitHubSponsorsPlatform.CredentialRejectedMessage("not-a-token");
        await Assert.That(message).Contains("no recognized prefix");
        await Assert.That(message).Contains("truncated");
    }

    [Test]
    [Arguments(429)]
    [Arguments(403)]
    public async Task RateLimitExhausted_ThrowsRateLimited(int status)
    {
        // GitHub reports a spent primary budget as 429, or as 403 with the remaining counter at zero.
        var reset = new DateTimeOffset(DateTime.UtcNow.AddMinutes(23), TimeSpan.Zero)
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        using var client = new HttpClient(
            new StatusHandler(
                (HttpStatusCode) status,
                """{"message":"API rate limit exceeded"}""",
                [("x-ratelimit-remaining", "0"), ("x-ratelimit-reset", reset)]));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        RateLimitedException? thrown = null;
        try
        {
            await platform.FetchSponsorAccounts("SimonCropp", $"ghp_{new string('x', 36)}", log, Cancel.None);
        }
        catch (RateLimitedException exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("The limit resets at");
        await Assert.That(thrown.Message).Contains("re-running the build after the reset");
    }

    [Test]
    public async Task Forbidden_WithoutRateLimitHeaders_StaysOnTheGenericPath()
    {
        // A bare 403 is a permission failure, not a spent budget. Misclassifying it would tell the
        // author to wait for a reset that never arrives.
        using var client = new HttpClient(
            new StatusHandler(HttpStatusCode.Forbidden, """{"message":"Resource not accessible"}"""));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        MaintenanceFeeException? thrown = null;
        try
        {
            await platform.FetchSponsorAccounts("SimonCropp", $"ghp_{new string('x', 36)}", log, Cancel.None);
        }
        catch (MaintenanceFeeException exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown is RateLimitedException).IsFalse();
        await Assert.That(thrown!.Message).Contains("HTTP 403");
    }

    [Test]
    public async Task SecondaryRateLimit_DetectedByRetryAfter()
    {
        using var client = new HttpClient(
            new StatusHandler(
                HttpStatusCode.Forbidden,
                """{"message":"You have exceeded a secondary rate limit"}""",
                [("retry-after", "60")]));
        var platform = new GitHubSponsorsPlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        RateLimitedException? thrown = null;
        try
        {
            await platform.FetchSponsorAccounts("SimonCropp", $"ghp_{new string('x', 36)}", log, Cancel.None);
        }
        catch (RateLimitedException exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("retry after 60 seconds");
    }

    [Test]
    public async Task GraphQlRateLimited_ThrowsRateLimited()
    {
        // GraphQL reports a spent budget as a 200 carrying a RATE_LIMITED error, so the HTTP status
        // check never sees it.
        var json = """
        {
          "errors": [
            { "type": "RATE_LIMITED", "message": "API rate limit exceeded" }
          ]
        }
        """;
        var exception = Assert.Throws<RateLimitedException>(() => GitHubSponsorsPlatform.ParseResponse(json));
        await Assert.That(exception.Message).Contains("5,000 points per hour");
    }

    sealed class StatusHandler(HttpStatusCode status, string body, (string Name, string Value)[]? headers = null) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            foreach (var (name, value) in headers ?? [])
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }

    // --- Private sponsors are never bundled ---
    //
    // includePrivate:true is kept on the query so the count stays knowable, which means the filter
    // is entirely on this side. If privacyLevel ever stops being parsed, these are what catches it.

    [Test]
    public async Task ParseResponse_PrivacyLevel_Parsed()
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
                    "privacyLevel": "PUBLIC",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  },
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "2024-02-15T10:00:00Z",
                    "privacyLevel": "PRIVATE",
                    "sponsorEntity": { "__typename": "User", "login": "dave" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserSponsorships.Single(_ => _.Login == "alice").IsPrivate).IsFalse();
        await Assert.That(page.UserSponsorships.Single(_ => _.Login == "dave").IsPrivate).IsTrue();
    }

    [Test]
    public async Task ParseResponse_MissingPrivacyLevel_ReadsAsPublic()
    {
        // Absent has to mean public: if an unrecognized shape read as private, a field rename on
        // GitHub's side would silently empty the whole bundled list instead of failing loudly.
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
          }
        }
        """;
        var page = GitHubSponsorsPlatform.ParseResponse(json);
        await Assert.That(page.UserSponsorships.Single().IsPrivate).IsFalse();
    }

    [Test]
    public async Task FetchSponsorAccounts_ExcludesPrivateSponsorsAndReportsTheCount()
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
                    "privacyLevel": "PUBLIC",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  },
                  {
                    "isActive": true,
                    "isOneTimePayment": false,
                    "createdAt": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
                    "privacyLevel": "PRIVATE",
                    "sponsorEntity": { "__typename": "User", "login": "dave" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var engine = new StubBuildEngine();
        var sponsors = await platform.FetchSponsorAccounts("acmecorp", token: "fake", new TaskLoggingHelperFor(engine), Cancel.None);
        await Assert.That(sponsors).Contains("alice");
        await Assert.That(sponsors).DoesNotContain("dave");
        // The count is reported but never the login — the point of the exclusion is that the
        // identity doesn't leave the author's machine.
        var notice = engine.Messages.Single(_ => _.Message!.Contains("excluded from the bundled list"));
        await Assert.That(notice.Message!).Contains("1 private sponsor is");
        await Assert.That(notice.Message!).Contains("SponsorshipPrivateUntil");
        await Assert.That(notice.Message!).DoesNotContain("dave");
    }

    [Test]
    public async Task FetchSponsorAccounts_NoPrivateSponsors_ReportsNothing()
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
                    "privacyLevel": "PUBLIC",
                    "sponsorEntity": { "__typename": "User", "login": "alice" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var engine = new StubBuildEngine();
        await platform.FetchSponsorAccounts("acmecorp", token: "fake", new TaskLoggingHelperFor(engine), Cancel.None);
        await Assert.That(engine.Messages.Any(_ => _.Message!.Contains("excluded from the bundled list"))).IsFalse();
    }

    [Test]
    public async Task FetchSponsorAccounts_LapsedPrivateSponsor_CountedOnce()
    {
        // A private sponsorship that also fails the validity window is dropped by IsValidAt first,
        // so it must not inflate the private count — the author would be told to chase a sponsor
        // who isn't sponsoring any more.
        var json = """
        {
          "data": {
            "user": {
              "sponsorshipsAsMaintainer": {
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "isActive": false,
                    "isOneTimePayment": false,
                    "createdAt": "2020-01-15T10:00:00Z",
                    "privacyLevel": "PRIVATE",
                    "sponsorEntity": { "__typename": "User", "login": "dave" }
                  }
                ]
              }
            },
            "organization": null
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(json));
        var platform = new GitHubSponsorsPlatform(client);
        var engine = new StubBuildEngine();
        var sponsors = await platform.FetchSponsorAccounts("acmecorp", token: "fake", new TaskLoggingHelperFor(engine), Cancel.None);
        await Assert.That(sponsors).IsEmpty();
        await Assert.That(engine.Messages.Any(_ => _.Message!.Contains("excluded from the bundled list"))).IsFalse();
    }

    sealed class RequestCapturingHandler(string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancel);
            }

            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
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
