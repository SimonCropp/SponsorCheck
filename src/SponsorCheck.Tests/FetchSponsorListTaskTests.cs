// Every fixture here fails before any request is made: GitHub Sponsors and Polar both refuse to
// fetch without a token. That is enough to pin what the task does with a result, and it keeps the
// suite off the network.
public class FetchSponsorListTaskTests
{
    [Test]
    public async Task NoAccounts_FetchesNothing()
    {
        var engine = new StubBuildEngine();
        var task = new FetchSponsorListTask
        {
            BuildEngine = engine
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(task.Fetched).IsEmpty();
        await Assert.That(engine.Messages).IsEmpty();
    }

    [Test]
    public async Task AFailure_IsHandedBackAndNotReported()
    {
        // The projects that asked report it, each with the code its own fetch would have raised.
        // Reported here as well it would be an error no project owns, in a build of a targets file.
        var engine = new StubBuildEngine();
        var task = new FetchSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccount = "acmecorp"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).IsEmpty();

        var results = SharedFetchResult.FromItems(task.Fetched);
        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].Platform).IsEqualTo("GitHubSponsors");
        await Assert.That(results[0].Owner).IsEqualTo("acmecorp");
        await Assert.That(results[0].Credentials).IsEqualTo(SharedFetchResult.Fingerprint([]));
        var failure = results[0].Failure!.ToException();
        await Assert.That(failure).IsTypeOf<MissingCredentialException>();
        await Assert.That(failure.Message).Contains("GitHub Sponsors: API token required");
    }

    [Test]
    public async Task OneResultPerPlatform()
    {
        var task = new FetchSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccount = "acmecorp",
            PolarAccount = "acme"
        };

        await Assert.That(task.Execute()).IsTrue();

        var results = SharedFetchResult.FromItems(task.Fetched);
        await Assert.That(results.Select(_ => _.Platform)).IsEquivalentTo(["GitHubSponsors", "Polar"]);
        await Assert.That(results.Select(_ => _.Owner)).IsEquivalentTo(["acmecorp", "acme"]);
    }

    [Test]
    public async Task TheAccountIsTrimmed()
    {
        // The bundler trims when it merges the PackageReference and PackageVersion values, and it
        // only takes a result fetched for the account it resolved.
        var task = new FetchSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccount = "  acmecorp "
        };

        await Assert.That(task.Execute()).IsTrue();

        var results = SharedFetchResult.FromItems(task.Fetched);
        await Assert.That(results[0].Owner).IsEqualTo("acmecorp");
    }

    [Test]
    public async Task SaysItFetchesOncePerPlatform()
    {
        // The line the integration suite counts to tell how many times a build fetched.
        var engine = new StubBuildEngine();
        var task = new FetchSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccount = "acmecorp"
        };

        await Assert.That(task.Execute()).IsTrue();

        var lines = engine.Messages
            .Select(_ => _.Message)
            .Where(_ => _!.Contains("once for this build"))
            .ToList();
        await Assert.That(lines).HasSingleItem();
        await Assert.That(lines[0]).IsEqualTo("SponsorCheck: fetching the GitHubSponsors sponsor list of 'acmecorp' once for this build.");
    }
}
