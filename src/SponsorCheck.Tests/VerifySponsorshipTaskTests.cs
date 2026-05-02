public class VerifySponsorshipTaskTests
{
    static string WriteHashes(params (string platform, string account)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sponsorcheck-hashes-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, entries.Select(e => SponsorHasher.Hash(e.platform, e.account)).OrderBy(h => h, StringComparer.Ordinal));
        return path;
    }

    [Test]
    public async Task NoConfig_FailsWithSC001()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
    }

    [Test]
    public async Task IgnoredTrue_PassesWithSC003Warning()
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
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC003");
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
    public async Task InvalidSponsor_FailsWithSC004()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
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
    public async Task ExpiredLicense_FailsWithSC005()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC005");
    }

    [Test]
    public async Task BadLicenseFormat_FailsWithSC007()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
    }

    [Test]
    public async Task ConflictingModes_FailsWithSC002()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
    }

    [Test]
    public async Task ConflictingMetadataAcrossRefAndVer_FailsWithSC006()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC006");
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
    public async Task SponsorshipStartAfterPackDate_TrustsDeclaration()
    {
        var hashes = WriteHashes(("GitHubSponsors", "alice"));
        var packDate = Path.Combine(Path.GetTempPath(), $"pd-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(packDate, "2026-04-15");
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = hashes,
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-30"
        };
        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Messages.Any(m => m.Message?.Contains("trusting unverified sponsor") == true)).IsTrue();
    }

    [Test]
    public async Task SponsorshipStartEqualsPackDate_FallsThroughToHash()
    {
        var hashes = WriteHashes(("GitHubSponsors", "alice"));
        var packDate = Path.Combine(Path.GetTempPath(), $"pd-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(packDate, "2026-04-15");
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = hashes,
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-15"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
    }

    // Guards the contract: a hash present at pack time grandfathers the consumer for that version even after they stop sponsoring.
    [Test]
    public async Task LapsedSponsorAgainstAlreadyPaidVersion_StillPasses()
    {
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(("GitHubSponsors", "bob")),
            GitHubFromRef = "bob"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task SponsorshipStartBeforePackDate_StillEnforcesHash()
    {
        var hashes = WriteHashes(("GitHubSponsors", "alice"));
        var packDate = Path.Combine(Path.GetTempPath(), $"pd-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(packDate, "2026-04-15");
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = hashes,
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-01"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
    }

    [Test]
    public async Task SponsorshipStartInFuture_FailsWithSC014()
    {
        var hashes = WriteHashes(("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2099-01-01"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC014");
    }

    [Test]
    public async Task SponsorshipStartBadFormat_FailsWithSC013()
    {
        var hashes = WriteHashes(("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "yesterday"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC013");
    }

    [Test]
    public async Task LicenseExactlyAtMonthEnd_Passes()
    {
        // The license is for the whole month; if utcNow is anywhere within that month it passes.
        var path = WriteHashes(("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(null, "2026-05",
            new Dictionary<string, string?> { ["GitHubSponsors"] = null, ["OpenCollective"] = null, ["Polar"] = null }, null, "MyOssLib");
        var ok = DecisionApplier.Apply(decision, path, "", new TaskLoggingHelperFor(new StubBuildEngine()), new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
    }
}

internal sealed class TaskLoggingHelperFor : Microsoft.Build.Utilities.TaskLoggingHelper
{
    public TaskLoggingHelperFor(IBuildEngine engine) : base(new StubTask(engine)) { }
    sealed class StubTask : Microsoft.Build.Utilities.Task
    {
        public StubTask(IBuildEngine engine) => BuildEngine = engine;
        public override bool Execute() => true;
    }
}
