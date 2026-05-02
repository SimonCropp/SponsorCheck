namespace EnforceOssSponsorship.Tests;

public class VerifySponsorshipTaskTests
{
    static string WriteHashes(params (string platform, string account)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eoss-hashes-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, entries.Select(e => SponsorHasher.Hash(e.platform, e.account)).OrderBy(h => h, StringComparer.Ordinal));
        return path;
    }

    [Test]
    public async Task NoConfig_FailsWithEOSS001()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS001");
    }

    [Test]
    public async Task IgnoredTrue_PassesWithEOSS003Warning()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("EOSS003");
    }

    [Test]
    public async Task ValidSponsor_Passes()
    {
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice"), ("GitHubSponsors", "bob")),
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task InvalidSponsor_FailsWithEOSS004()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS004");
    }

    [Test]
    public async Task AnyPlatformMatchPasses()
    {
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("Polar", "acme")),
            GitHubFromRef = "not-a-sponsor",
            PolarFromRef = "acme"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task FutureLicense_Passes()
    {
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2099-12"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task ExpiredLicense_FailsWithEOSS005()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS005");
    }

    [Test]
    public async Task BadLicenseFormat_FailsWithEOSS007()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "not-a-date"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS007");
    }

    [Test]
    public async Task ConflictingModes_FailsWithEOSS002()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS002");
    }

    [Test]
    public async Task ConflictingMetadataAcrossRefAndVer_FailsWithEOSS006()
    {
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            GitHubFromRef = "alice",
            GitHubFromVer = "bob"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS006");
    }

    [Test]
    public async Task CpmMetadataAlone_Works()
    {
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "alice")),
            GitHubFromVer = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task LicenseExactlyAtMonthEnd_Passes()
    {
        // The license is for the whole month; if utcNow is anywhere within that month it passes.
        var path = WriteHashes(("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(null, "2026-05",
            new Dictionary<string, string?> { ["GitHubSponsors"] = null, ["OpenCollective"] = null, ["Polar"] = null }, "MyOssLib");
        var ok = DecisionApplier.Apply(decision, path, new TaskLoggingHelperFor(new StubBuildEngine()), new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
    }
}

internal sealed class TaskLoggingHelperFor : Microsoft.Build.Utilities.TaskLoggingHelper
{
    public TaskLoggingHelperFor(Microsoft.Build.Framework.IBuildEngine engine) : base(new StubTask(engine)) { }
    sealed class StubTask : Microsoft.Build.Utilities.Task
    {
        public StubTask(Microsoft.Build.Framework.IBuildEngine engine) => BuildEngine = engine;
        public override bool Execute() => true;
    }
}
