public class VerifySponsorshipTaskTests
{
    static string WriteHashes(TempDirectory dir, params (string platform, string account)[] entries)
    {
        var path = Path.Combine(dir, "hashes.txt");
        File.WriteAllLines(path, entries.Select(e => SponsorHasher.Hash(e.platform, e.account)).OrderBy(h => h, StringComparer.Ordinal));
        return path;
    }

    static string WriteAuthorAccounts(TempDirectory dir, params (string platform, string account)[] entries)
    {
        var path = Path.Combine(dir, "AuthorAccounts.txt");
        AuthorAccountsFile.Write(path, entries.Select(e => new KeyValuePair<string, string>(e.platform, e.account)));
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
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
        await Verify(engine);
    }

    [Test]
    public async Task SC001_MessageIncludesAuthorSponsorUrls()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme"))
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("https://github.com/sponsors/acmecorp");
        await Assert.That(message).Contains("https://opencollective.com/acme-org");
        await Assert.That(message).Contains("https://polar.sh/acme");
        await Assert.That(message).DoesNotContain("opensourcemaintenancefee.org");
        await Verify(engine);
    }

    [Test]
    public async Task SC003_MessageIncludesAuthorSponsorUrls()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        var message = engine.Warnings[0].Message!;
        await Assert.That(message).Contains("https://github.com/sponsors/acmecorp");
        await Assert.That(message).DoesNotContain("opensourcemaintenancefee.org");
        await Verify(engine);
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
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC003");
        await Verify(engine);
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
        await Verify(engine);
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
        await Verify(engine);
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
        await Verify(engine);
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
        await Verify(engine);
    }

    [Test]
    public async Task MetadataOnBothRefAndVer_FailsWithSC006()
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
    public async Task MetadataOnBothRefAndVerWithSameValue_FailsWithSC006()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "alice",
            GitHubFromVer = "alice"
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
        await Verify(engine);
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
        await Verify(engine);
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
        await Verify(engine);
    }

    [Test]
    public async Task SponsorshipStartInFuture_FailsWithSC011()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC011");
        await Verify(engine);
    }

    [Test]
    public async Task SponsorshipStartBadFormat_FailsWithSC010()
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
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC010");
        await Verify(engine);
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
        var ok = DecisionApplier.Apply(decision, path, "", [], new Dictionary<string, Severity>(), new Dictionary<string, string>(), new TaskLoggingHelperFor(new StubBuildEngine()), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
    }

    [Test]
    public async Task LicenseInFinalSubSecondOfMonth_Passes()
    {
        // The cutoff is the start of the next month, not last-day 23:59:59. Any instant
        // strictly before the next month — including 23:59:59.9999999 on the last day —
        // must still pass. With the previous whole-second cutoff, a build at .500 would
        // have been incorrectly flagged SC005.
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
        var lastTickOfMonth = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(decision, path, "", [], new Dictionary<string, Severity>(), new Dictionary<string, string>(), new TaskLoggingHelperFor(engine), lastTickOfMonth);
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task LicenseAtFirstInstantOfNextMonth_FailsWithSC005()
    {
        // Mirror of LicenseInFinalSubSecondOfMonth_Passes: the very next tick — start of
        // the following month — is the first instant outside the licensed range.
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
        var startOfNextMonth = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(decision, path, "", [], new Dictionary<string, Severity>(), new Dictionary<string, string>(), new TaskLoggingHelperFor(engine), startOfNextMonth);
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC005");
    }

    static string WriteOverrides(TempDirectory dir, params (string code, Severity severity)[] entries)
    {
        var path = Path.Combine(dir, "SeverityOverrides.txt");
        var dict = entries.ToDictionary(_ => _.code, _ => _.severity, StringComparer.Ordinal);
        SeverityOverrideFile.Write(path, dict);
        return path;
    }

    static string WriteMessageOverrides(TempDirectory dir, params (string code, string message)[] entries)
    {
        var path = Path.Combine(dir, "MessageOverrides.json");
        var dict = entries.ToDictionary(_ => _.code, _ => _.message, StringComparer.Ordinal);
        MessageOverrideFile.Write(path, dict);
        return path;
    }

    [Test]
    public async Task SC001_DowngradedToWarning_BuildPasses()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            SeverityOverridesPath = WriteOverrides(dir, ("SC001", Severity.Warning))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC001");
    }

    [Test]
    public async Task SC003_PromotedToError_BuildFails()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            SeverityOverridesPath = WriteOverrides(dir, ("SC003", Severity.Error))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Warnings).IsEmpty();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
    }

    [Test]
    public async Task SC004_DowngradedToWarning_BuildPasses()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory",
            SeverityOverridesPath = WriteOverrides(dir, ("SC004", Severity.Warning))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC004");
    }

    [Test]
    public async Task SC005_DowngradedToMessage_BuildPasses()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2000-01",
            SeverityOverridesPath = WriteOverrides(dir, ("SC005", Severity.Message))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).IsEmpty();
        await Assert.That(engine.Messages.Any(_ => _.Code == "SC005")).IsTrue();
    }

    [Test]
    public async Task SC001_MessageOverride_ReplacesDefaultText()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC001", "Please sponsor MyOssLib!"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
        var message = engine.Errors[0].Message!;
        // The Name prefix and docs URL suffix still wrap; the inner text is the override.
        await Assert.That(message).Contains("No license specified.");
        await Assert.That(message).Contains("Please sponsor MyOssLib!");
        await Assert.That(message).DoesNotContain("requires one license-mode metadata");
        await Assert.That(message).Contains("#sc001");
    }

    [Test]
    public async Task SC003_MessageOverride_ReplacesDefaultText()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC003", "You agreed not to free-ride."))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Message!).Contains("You agreed not to free-ride.");
        await Assert.That(engine.Warnings[0].Message!).DoesNotContain("Build is allowed but is in breach");
    }

    [Test]
    public async Task SeverityAndMessageOverride_BothApplied()
    {
        // Authors can combine: downgrade SC001 to warning AND replace its text.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            SeverityOverridesPath = WriteOverrides(dir, ("SC001", Severity.Warning)),
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC001", "soft nudge"))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC001");
        await Assert.That(engine.Warnings[0].Message!).Contains("soft nudge");
    }

    [Test]
    public async Task MessageOverride_OnNonTrippedCode_HasNoEffect()
    {
        // SC001 message override is set, but the consumer trips SC004 — the SC001 override is
        // never read.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory",
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC001", "should not appear"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
        await Assert.That(engine.Errors[0].Message!).DoesNotContain("should not appear");
    }

    [Test]
    public async Task NonOverrideableCode_IgnoresEntryInSidecar()
    {
        // Verifier is tolerant of sidecar entries for non-overrideable codes (SC002 here) —
        // bundler-side validation is the source of truth, so any sneaky entry is silently dropped.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "SeverityOverrides.txt");
        await File.WriteAllLinesAsync(path, ["SC002=warning"]);
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice",
            SeverityOverridesPath = path
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
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
