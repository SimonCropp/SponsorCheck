using TaskItem = Microsoft.Build.Utilities.TaskItem;
using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

public class SharedFetchResultTests
{
    static TaskLoggingHelper Log() =>
        new TaskLoggingHelperFor(new StubBuildEngine());

    static TokenCandidate[] Tokens(params string[] values) =>
        [.. values.Select(_ => new TokenCandidate(_, "a test"))];

    [Test]
    public async Task Run_CarriesTheAccountsAndTheCredentialsItUsed()
    {
        var platform = new FakePlatform(_ => ["alice", "bob"]);
        var tokens = Tokens("first-token");

        var result = await SharedFetchResult.Run(platform, "acmecorp", tokens, Log());

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Platform).IsEqualTo("GitHubSponsors");
        await Assert.That(result.Owner).IsEqualTo("acmecorp");
        await Assert.That(result.Accounts).IsEquivalentTo(["alice", "bob"]);
        await Assert.That(result.Credentials).IsEqualTo(SharedFetchResult.Fingerprint(tokens));
        await Assert.That(platform.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Run_FallsThroughToTheNextCandidateToken()
    {
        // The stale-env-var case the bundler has always handled: the first candidate is rejected,
        // the second works. Sharing the fetch must not lose that.
        var platform = new FakePlatform(_ =>
        {
            if (_ == "stale")
            {
                throw new InvalidCredentialException("rejected");
            }

            return ["alice"];
        });

        var result = await SharedFetchResult.Run(platform, "acmecorp", Tokens("stale", "fresh"), Log());

        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.Accounts).IsEquivalentTo(["alice"]);
        await Assert.That(platform.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task Run_AFailureIsHandedBackAsTheExceptionItWas()
    {
        // The exception type is what the bundler maps to a diagnostic code, so each one has to come
        // back out as itself: SC102, SC107, SC108 and SC100 respectively.
        Exception[] failures =
        [
            new MissingCredentialException("no token"),
            new RateLimitedException("limit exhausted"),
            new MaintenanceFeeException("HTTP 500")
        ];
        foreach (var failure in failures)
        {
            var platform = new FakePlatform(_ => throw failure);

            var result = await SharedFetchResult.Run(platform, "acmecorp", Tokens("token"), Log());

            await Assert.That(result.Failure).IsNotNull();
            var rethrown = result.Failure!.ToException();
            await Assert.That(rethrown.GetType()).IsEqualTo(failure.GetType());
            await Assert.That(rethrown.Message).IsEqualTo(failure.Message);
            await Assert.That(result.Accounts).IsEmpty();
        }
    }

    [Test]
    public async Task Run_ARejectedCredentialNamesWhereItWasStored()
    {
        // The source is grafted on by the fetch, not by the platform, so it has to be in the
        // message that travels: the project reading the result back never sees the original.
        var platform = new FakePlatform(_ => throw new InvalidCredentialException("rejected."));

        var result = await SharedFetchResult.Run(platform, "acmecorp", Tokens("token"), Log());

        var rethrown = result.Failure!.ToException();
        await Assert.That(rethrown).IsTypeOf<InvalidCredentialException>();
        await Assert.That(rethrown.Message).IsEqualTo("rejected. The rejected credential was read from a test.");
    }

    [Test]
    public async Task Run_AFailureThatIsNotAPlatformErrorIsCarriedToo()
    {
        var platform = new FakePlatform(_ => throw new HttpRequestException("No such host is known."));

        var result = await SharedFetchResult.Run(platform, "acmecorp", Tokens("token"), Log());

        var rethrown = result.Failure!.ToException();
        await Assert.That(rethrown).IsTypeOf<SharedFetchException>();
        await Assert.That(rethrown.Message).IsEqualTo("No such host is known.");
    }

    [Test]
    public async Task ASuccess_RoundTripsThroughAnItem()
    {
        // '%41' is 'A' to anything that unescapes it, and ';' splits an MSBuild list.
        string[] accounts = ["alice", "per%41cent", "semi;colon", "ünïcode"];
        var result = new SharedFetchResult("GitHubSponsors", "acme%20corp", "0123456789abcdef", accounts);

        var read = SharedFetchResult.FromItems([Copy(result.ToItem())]);

        await Assert.That(read).HasSingleItem();
        await Assert.That(read[0].Platform).IsEqualTo("GitHubSponsors");
        await Assert.That(read[0].Owner).IsEqualTo("acme%20corp");
        await Assert.That(read[0].Credentials).IsEqualTo("0123456789abcdef");
        await Assert.That(read[0].Accounts).IsEquivalentTo(accounts);
        await Assert.That(read[0].Failure).IsNull();
    }

    [Test]
    public async Task AnEmptyList_RoundTripsThroughAnItem()
    {
        var result = new SharedFetchResult("Polar", "acme", "none", []);

        var read = SharedFetchResult.FromItems([Copy(result.ToItem())]);

        await Assert.That(read).HasSingleItem();
        await Assert.That(read[0].Accounts).IsEmpty();
        await Assert.That(read[0].Failure).IsNull();
    }

    [Test]
    public async Task AFailure_RoundTripsThroughAnItem()
    {
        var message =
            """
            GitHub GraphQL HTTP 500: {"message":"50% of $(Requests) failed; retry"}
            second line
            """;
        var failure = SharedFetchFailure.From(new RateLimitedException(message));
        var result = new SharedFetchResult("GitHubSponsors", "acmecorp", "0123456789abcdef", [], failure);

        var read = SharedFetchResult.FromItems([Copy(result.ToItem())]);

        await Assert.That(read).HasSingleItem();
        var rethrown = read[0].Failure!.ToException();
        await Assert.That(rethrown).IsTypeOf<RateLimitedException>();
        await Assert.That(rethrown.Message).IsEqualTo(message);
    }

    [Test]
    public async Task AnItemThatDoesNotParse_IsLeftOut()
    {
        var good = new SharedFetchResult("Polar", "acme", "none", ["alice"]).ToItem();
        var noCredentials = new TaskItem("GitHubSponsors");
        var badAccounts = new SharedFetchResult("OpenCollective", "acme", "none", ["alice"]).ToItem();
        badAccounts.SetMetadata("Accounts", "not base64!");

        var read = SharedFetchResult.FromItems([noCredentials, badAccounts, good]);

        await Assert.That(read).HasSingleItem();
        await Assert.That(read[0].Platform).IsEqualTo("Polar");
    }

    [Test]
    public async Task IsFor_NeedsThePlatformTheAccountAndTheCredentials()
    {
        var result = new SharedFetchResult("GitHubSponsors", "acmecorp", "0123456789abcdef", []);

        await Assert.That(result.IsFor("GitHubSponsors", "acmecorp", "0123456789abcdef")).IsTrue();
        // The registry hands back the canonical id, but an item spec is whatever was written.
        await Assert.That(result.IsFor("githubsponsors", "acmecorp", "0123456789abcdef")).IsTrue();
        await Assert.That(result.IsFor("Polar", "acmecorp", "0123456789abcdef")).IsFalse();
        await Assert.That(result.IsFor("GitHubSponsors", "someoneelse", "0123456789abcdef")).IsFalse();
        await Assert.That(result.IsFor("GitHubSponsors", "acmecorp", "none")).IsFalse();
    }

    [Test]
    public async Task Fingerprint_TellsCredentialsApartWithoutContainingThem()
    {
        var token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";
        var fingerprint = SharedFetchResult.Fingerprint(Tokens(token));

        await Assert.That(fingerprint).IsEqualTo(SharedFetchResult.Fingerprint(Tokens(token)));
        await Assert.That(fingerprint).IsNotEqualTo(SharedFetchResult.Fingerprint(Tokens("ghp_another")));
        await Assert.That(fingerprint.Length).IsEqualTo(16);
        await Assert.That(token).DoesNotContain(fingerprint);
        await Assert.That(fingerprint).DoesNotContain("ghp_");
    }

    [Test]
    public async Task Fingerprint_CoversEveryCandidateInOrder()
    {
        // The candidates are tried in order and the first that works decides the list, so two
        // projects whose candidates differ at all are not asking the same question.
        var both = SharedFetchResult.Fingerprint(Tokens("one", "two"));

        await Assert.That(both).IsNotEqualTo(SharedFetchResult.Fingerprint(Tokens("one")));
        await Assert.That(both).IsNotEqualTo(SharedFetchResult.Fingerprint(Tokens("two", "one")));
    }

    [Test]
    public async Task Fingerprint_NoCandidates_IsItsOwnValue()
    {
        var none = SharedFetchResult.Fingerprint([]);

        await Assert.That(none).IsEqualTo("none");
        await Assert.That(none).IsNotEqualTo(SharedFetchResult.Fingerprint(Tokens("")));
    }

    // MSBuild hands a target's outputs on as copies, which is where escaped metadata would change.
    static ITaskItem Copy(ITaskItem item) =>
        new TaskItem(item);

    sealed class FakePlatform(Func<string?, IReadOnlyList<string>> fetch) :
        ISponsorshipPlatform
    {
        public int Calls { get; private set; }

        public string Id => "GitHubSponsors";

        public string SponsorPageUrl(string ownerAccount) =>
            $"https://example.invalid/{ownerAccount}";

        public Task<IReadOnlyList<string>> FetchSponsorAccounts(string ownerAccount, string? token, TaskLoggingHelper log, Cancel cancel)
        {
            Calls++;
            try
            {
                return Task.FromResult(fetch(token));
            }
            catch (Exception exception)
            {
                return Task.FromException<IReadOnlyList<string>>(exception);
            }
        }
    }
}
