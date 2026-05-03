public class VerifySponsorshipTaskTests
{
    static string WriteHashes(TempDirectory dir, params (string platform, string account)[] entries)
    {
        var path = Path.Combine(dir, "hashes.txt");
        File.WriteAllLines(path, entries.Select(e => SponsorHasher.Hash(e.platform, e.account)).OrderBy(h => h, StringComparer.Ordinal));
        return path;
    }

    [Test]
    public async Task NoConfig_FailsWithSC001()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
    }

    [Test]
    public async Task IgnoredTrue_PassesWithSC003Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
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
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice"), ("GitHubSponsors", "bob")),
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task InvalidSponsor_FailsWithSC004()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
    }

    [Test]
    public async Task AnyPlatformMatchPasses()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("Polar", "acme")),
            GitHubFromRef = "not-a-sponsor",
            PolarFromRef = "acme"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task FutureLicense_Passes()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2099-12"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task ExpiredLicense_FailsWithSC005()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC005");
    }

    [Test]
    public async Task BadLicenseFormat_FailsWithSC007()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "not-a-date"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
    }

    [Test]
    public async Task ConflictingModes_FailsWithSC002()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
    }

    [Test]
    public async Task ConflictingMetadataAcrossRefAndVer_FailsWithSC006()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "alice",
            GitHubFromVer = "bob"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC006");
    }

    [Test]
    public async Task CpmMetadataAlone_Works()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromVer = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task SponsorshipStartAfterPackDate_TrustsDeclaration()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var packDate = Path.Combine(dir, "packdate.txt");
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
        await Assert.That(engine.Messages.Any(_ => _.Message?.Contains("trusting unverified sponsor") == true)).IsTrue();
    }

    [Test]
    public async Task SponsorshipStartEqualsPackDate_FallsThroughToHash()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var packDate = Path.Combine(dir, "packdate.txt");
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
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "bob")),
            GitHubFromRef = "bob"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task SponsorshipStartBeforePackDate_StillEnforcesHash()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var packDate = Path.Combine(dir, "packdate.txt");
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
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
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
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
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
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(
            null,
            "2026-05",
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = null,
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");
        var ok = DecisionApplier.Apply(decision, path, "", new TaskLoggingHelperFor(new StubBuildEngine()), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
    }
}

internal sealed class TaskLoggingHelperFor(IBuildEngine engine) :
    Microsoft.Build.Utilities.TaskLoggingHelper(new StubTask(engine))
{
    sealed class StubTask : Microsoft.Build.Utilities.Task
    {
        public StubTask(IBuildEngine engine) => BuildEngine = engine;
        public override bool Execute() => true;
    }
}
