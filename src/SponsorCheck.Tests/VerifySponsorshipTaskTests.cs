public class VerifySponsorshipTaskTests
{
    // Stable test paths so snapshots don't depend on the working directory or OS path style.
    const string consumerProject = "C:/Consumer/MyApp.csproj";
    const string directoryPackagesProps = "C:/Consumer/Directory.Packages.props";

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

    static string WriteExemptions(TempDirectory dir, params (string name, string message)[] entries) =>
        WriteBoundedExemptions(dir, [.. entries.Select(_ => (_.name, _.message, (int?) null))]);

    static string WriteBoundedExemptions(TempDirectory dir, params (string name, string message, int? maxTermMonths)[] entries)
    {
        var path = Path.Combine(dir, "Exemptions.json");
        var dict = entries.ToDictionary(
            _ => _.name,
            _ => new ExemptionDefinition(_.message, _.maxTermMonths),
            StringComparer.OrdinalIgnoreCase);
        SponsorshipExemptionsFile.Write(path, dict);
        return path;
    }

    static ConsumerContext NonCpmContext(string packageId = "MyOssLib", string version = "1.2.3") =>
        new(ConsumerMode.NonCpm, consumerProject, "", packageId, version);

    static ConsumerContext CpmContext(string packageId = "MyOssLib", string version = "1.2.3") =>
        new(ConsumerMode.Cpm, consumerProject, directoryPackagesProps, packageId, version);

    static ConsumerContext OwnerContext(string packageId = "MyOssLib", string version = "1.2.3") =>
        new(ConsumerMode.Owner, consumerProject, "", packageId, version, "acme");

    static LicenseDecision LicensedDecision(string licensedUntil) =>
        LicenseModeResolver.Resolve(
            null,
            licensedUntil,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = null,
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");

    // Empty lazy sidecars for direct DecisionApplier.Apply calls. They mirror the Lazy wrapping
    // VerifySponsorshipTask does; forcing .Value just yields an empty collection (no file read).
    static readonly Lazy<IReadOnlyList<AuthorAccount>> noAuthorAccounts = new(() => []);
    static readonly Lazy<IReadOnlyDictionary<string, ExemptionDefinition>> noExemptions = new(() => new Dictionary<string, ExemptionDefinition>());
    static readonly Lazy<IReadOnlyDictionary<string, Severity>> noSeverityOverrides = new(() => new Dictionary<string, Severity>());
    static readonly Lazy<IReadOnlyDictionary<string, string>> noMessageOverrides = new(() => new Dictionary<string, string>());

    [Test]
    public async Task NoConfig_FailsWithSC001()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
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
    public async Task SC005_MessageIncludesAuthorSponsorUrls()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
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
    public async Task IgnoredTrue_PassesWithSC005Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC005");
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
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice"), ("GitHubSponsors", "bob")),
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task InvalidSponsor_FailsWithSC007()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    [Test]
    public async Task CpmInvalidSponsor_FailsWithSC008()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromVer = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC008");
        await Verify(engine);
    }

    [Test]
    public async Task CpmInvalidSponsor_MultiplePlatformsConfigured_ShowsAllSponsorUrls()
    {
        // Multi-platform variant of CpmInvalidSponsor_FailsWithSC008: SC008 message lists every
        // configured platform's sponsor URL.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromVer = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC008");
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
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("Polar", "acme")),
            GitHubFromRef = "not-a-sponsor",
            PolarFromRef = "acme"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task InvalidSponsor_OnePlatformConfigured_ShowsSponsorUrl()
    {
        // When the consumer's sponsor account doesn't match and the author has a single platform
        // enabled, SC007 renders the inline single-line "Sponsor at <url>" form (no colon, no
        // newline + indent). The multi-platform variant uses the "Sponsor at:\n  <url>\n  <url>" block.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("Sponsor at https://github.com/sponsors/acmecorp");
        await Verify(engine);
    }

    [Test]
    public async Task InvalidSponsor_MultiplePlatformsConfigured_ShowsAllSponsorUrls()
    {
        // Multi-platform author: every configured platform's sponsor URL appears in the SC007
        // "Sponsor at:" block so the consumer can pick whichever channel they prefer.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("Sponsor at:");
        await Assert.That(message).Contains("https://github.com/sponsors/acmecorp");
        await Assert.That(message).Contains("https://opencollective.com/acme-org");
        await Assert.That(message).Contains("https://polar.sh/acme");
        await Verify(engine);
    }

    [Test]
    public async Task MultiplePlatformAccounts_NoneMatch_FailsWithSC007ListingAllAttempts()
    {
        // Consumer declared sponsor accounts on all three platforms; none are in the bundled list.
        // The SC007 message's "Tried:" line must enumerate each attempted (platform, account) pair
        // so the consumer can see exactly which lookups were performed.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromRef = "mallory",
            OpenCollectiveFromRef = "wrong-org",
            PolarFromRef = "wrong-handle"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("GitHubSponsors=mallory");
        await Assert.That(message).Contains("OpenCollective=wrong-org");
        await Assert.That(message).Contains("Polar=wrong-handle");
        await Verify(engine);
    }

    // A month that is in the future but inside the one-year cap. The task reads the real clock, so
    // these happy-path tests compute the value rather than hardcoding one that would age out.
    static string WithinCapMonth => DateTime.UtcNow.AddMonths(6).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Test]
    public async Task FutureLicense_Passes()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = WithinCapMonth
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task ExpiredLicense_FailsWithSC009()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromRef = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
        await Verify(engine);
    }

    [Test]
    public async Task ExpiredLicense_MultiplePlatformsConfigured_ShowsAllSponsorUrls()
    {
        // Multi-platform variant: SC009 should also list every author sponsor URL so the consumer
        // can choose to switch from time-bounded license to sponsorship instead of renewing.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LicensedUntilFromRef = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
        await Verify(engine);
    }

    [Test]
    public async Task CpmExpiredLicense_FailsWithSC010()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromVer = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC010");
        await Verify(engine);
    }

    [Test]
    public async Task CpmExpiredLicense_MultiplePlatformsConfigured_ShowsAllSponsorUrls()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LicensedUntilFromVer = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC010");
        await Verify(engine);
    }

    [Test]
    public async Task BadLicenseFormat_FailsWithSC011()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "not-a-date"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC011");
        await Verify(engine);
    }

    [Test]
    public async Task CpmBadLicenseFormat_FailsWithSC012()
    {
        // CPM sibling of SC011: SponsorshipLicensedUntil sits on <PackageVersion> in
        // Directory.Packages.props instead of <PackageReference> in the consumer csproj.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromVer = "not-a-date"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC012");
        await Verify(engine);
    }

    [Test]
    public async Task ConflictingModes_FailsWithSC003()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
        await Verify(engine);
    }

    [Test]
    public async Task CpmConflictingModes_FailsWithSC004()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromVer = "true",
            GitHubFromVer = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC004");
        await Verify(engine);
    }

    [Test]
    public async Task MetadataOnBothRefAndVer_FailsWithSC020()
    {
        // Under the post-v0.3 placement rule SC020 fires before SC019 ever has a chance to merge —
        // the wrong-side value alone is a placement violation regardless of what the right side carries.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "alice",
            GitHubFromVer = "bob"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
    }

    [Test]
    public async Task CpmMetadataAlone_Works()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromVer = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task CpmIgnored_Passes()
    {
        // CPM happy path for SponsorshipLicenseIgnored: metadata on PackageVersion is the right
        // location and the build should warn (SC005) rather than error.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromVer = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC006");
    }

    [Test]
    public async Task CpmIgnored_MultiplePlatformsConfigured_WarnsWithSC006()
    {
        // Multi-platform variant of CpmIgnored_Passes: SC006 warning body renders all configured
        // platform sponsor URLs.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            IgnoredFromVer = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC006");
        await Verify(engine);
    }

    [Test]
    public async Task CpmLicensedUntilFuture_Passes()
    {
        // CPM happy path for SponsorshipLicensedUntil on PackageVersion.
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromVer = WithinCapMonth
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task CpmSponsorshipStartAfterPackDate_TrustsDeclaration()
    {
        // CPM happy path for SponsorshipStart on PackageVersion: same trust-attestation behaviour
        // as the non-CPM equivalent, just routed via *FromVer.
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var packDate = Path.Combine(dir, "packdate.txt");
        await File.WriteAllTextAsync(packDate, "2026-04-15");
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            SponsorHashListPath = hashes,
            PackDatePath = packDate,
            GitHubFromVer = "carol",
            SponsorshipStartFromVer = "2026-04-30"
        };
        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Messages.Any(_ => _.Code == "SC017")).IsTrue();
    }

    [Test]
    public async Task CpmNoConfig_FailsWithSC002()
    {
        // Locks in that under CPM the SC002 message names <PackageVersion> and the props file
        // path, with the rendered example using <PackageVersion Include=...>.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
        await Verify(engine);
    }

    [Test]
    public async Task CpmNoConfig_MultiplePlatformsConfigured_FailsWithSC002()
    {
        // Multi-platform variant of CpmNoConfig_FailsWithSC002: SC002 message renders one
        // "Sponsor on..." option per platform and lists all sponsor URLs.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
        await Verify(engine);
    }

    [Test]
    public async Task CpmMetadataOnBothRefAndVer_FailsWithSC020()
    {
        // Under CPM the PackageReference side is wrong regardless of what PackageVersion says.
        // SC020 fires from the wrong-side check before the merger ever runs (so SC019 is unreachable here).
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            IgnoredFromVer = "true"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
        // The right-side value didn't get to the merger so SC019 never fires.
        await Assert.That(engine.Errors.Any(_ => _.Code == "SC019")).IsFalse();
    }

    [Test]
    public async Task CpmMultipleMetadataMisplaced_FailsWithSingleSC020ListingAll()
    {
        // The placement check aggregates: if a CPM consumer wrongly puts two attributes on
        // PackageReference, both names appear in one SC020 message rather than spamming the log.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice",
            LicensedUntilFromRef = "2099-12"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("SponsorshipLicenseIgnored");
        await Assert.That(message).Contains("GitHubSponsorAccount");
        await Assert.That(message).Contains("SponsorshipLicensedUntil");
    }

    [Test]
    public async Task CpmRendering_PrefersPackageVersionVersionForExample()
    {
        // Under CPM the rendered example must use the <PackageVersion> Version, not the
        // (potentially stale or missing) PackageReference Version. We seed both with distinct
        // values so a regression that picked the wrong side would show "9.9.9" in the snippet.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromRef = "9.9.9",
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("Version=\"1.2.3\"");
        await Assert.That(message).DoesNotContain("Version=\"9.9.9\"");
    }

    [Test]
    public async Task NonCpmRendering_PrefersPackageReferenceVersionForExample()
    {
        // Mirror of CpmRendering_PrefersPackageVersionVersionForExample: without CPM the
        // PackageReference Version wins.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            PackageVersionFromVer = "9.9.9",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("Version=\"1.2.3\"");
        await Assert.That(message).DoesNotContain("Version=\"9.9.9\"");
    }

    [Test]
    public async Task CpmMetadataOnPackageReference_FailsWithSC020()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
        await Verify(engine);
    }

    [Test]
    public async Task NonCpmMetadataOnPackageVersion_FailsWithSC020()
    {
        // Mirror image: when CPM is off the metadata must live on PackageReference. Putting it on
        // PackageVersion (which usually doesn't even resolve outside CPM) is a placement violation.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromVer = "true"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
        await Verify(engine);
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
            ConsumerProjectPath = consumerProject,
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-15"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
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
            ConsumerProjectPath = consumerProject,
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-01"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    [Test]
    public async Task SponsorshipStartInFuture_FailsWithSC015()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2099-01-01"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC015");
        await Verify(engine);
    }

    [Test]
    public async Task CpmSponsorshipStartInFuture_FailsWithSC016()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromVer = "carol",
            SponsorshipStartFromVer = "2099-01-01"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC016");
        await Verify(engine);
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "yesterday"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC013");
        await Verify(engine);
    }

    [Test]
    public async Task CpmSponsorshipStartBadFormat_FailsWithSC014()
    {
        using var dir = new TempDirectory();
        var hashes = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = hashes,
            PackDatePath = "",
            GitHubFromVer = "carol",
            SponsorshipStartFromVer = "yesterday"
        };
        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC014");
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
            null,
            null,
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = null,
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(new StubBuildEngine()), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
    }

    [Test]
    public async Task LicenseInFinalSubSecondOfMonth_Passes()
    {
        // The cutoff is the start of the next month, not last-day 23:59:59. Any instant
        // strictly before the next month — including 23:59:59.9999999 on the last day —
        // must still pass. With the previous whole-second cutoff, a build at .500 would
        // have been incorrectly flagged SC009.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(
            null,
            "2026-05",
            null,
            null,
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
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), lastTickOfMonth);
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task LicenseAtFirstInstantOfNextMonth_FailsWithSC009()
    {
        // Mirror of LicenseInFinalSubSecondOfMonth_Passes: the very next tick — start of
        // the following month — is the first instant outside the licensed range.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(
            null,
            "2026-05",
            null,
            null,
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
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), startOfNextMonth);
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
    }

    [Test]
    public async Task LicenseAtCalendarExtreme_Passes()
    {
        // Deciding expiry by materializing start-of-next-month via AddMonths(1) overflowed
        // DateTime.MaxValue and threw; the calendar-field comparison passes cleanly. "now" is
        // DateTime.MaxValue — the latest instant a build can occur — and "9999-12" is both within
        // the licensed month and inside the one-year cap from there, so it must pass. This is the
        // only clock at which "9999-12" is still a legal value; see LicenseBeyondOneYear_*.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicensedDecision("9999-12");
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), DateTime.MaxValue);
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task PerpetualLicense_9999_12_ThroughTask_FailsWithSC035()
    {
        // Regression for the full verifier path (real clock): "9999-12" is the natural "perpetual
        // license" sentinel and is exactly what the one-year cap exists to reject. It must fail as
        // a coded SC035 diagnostic — not as the code-less overflow error ApplyLicensed used to
        // throw when it materialized start-of-next-month via AddMonths(1).
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromRef = "9999-12"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC035");
    }

    [Test]
    public async Task LicenseBeyondOneYear_FailsWithSC035()
    {
        // SponsorshipLicensedUntil is an unverified self-attestation, so it is capped at one year
        // from the build clock. A fixed utcNow keeps the rendered cap month deterministic.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(LicensedDecision("2030-01"), path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC035");
        await Verify(engine);
    }

    [Test]
    public async Task CpmLicenseBeyondOneYear_FailsWithSC036()
    {
        // CPM sibling of SC035: the remediation block points at Directory.Packages.props and
        // renders <PackageVersion> rather than <PackageReference>.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(LicensedDecision("2030-01"), path, "", CpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC036");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_LicenseBeyondOneYear_FailsWithSC037()
    {
        // Owner-mode sibling of SC035/SC036: the cap applies to the owner-prefixed global property.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(LicensedDecision("2030-01"), path, "", OwnerContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC037");
        await Verify(engine);
    }

    [Test]
    public async Task LicenseExactlyOneYearOut_Passes()
    {
        // The cap is inclusive of the same month next year — the last value a consumer can declare.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(LicensedDecision("2027-05"), path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task LicenseOneMonthPastCap_FailsWithSC035()
    {
        // Mirror of LicenseExactlyOneYearOut_Passes: the next month over is the first rejected value.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(LicensedDecision("2027-06"), path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC035");
        await Assert.That(engine.Errors[0].Message).Contains("maximum 2027-05");
    }

    [Test]
    public async Task ExpiredLicense_NearMaxDate_FailsWithoutOverflow()
    {
        // The expired branch computes the licensed month's last day for the message. Guard that this
        // stays overflow-safe at the calendar extreme: a "9999-11" license evaluated in 9999-12
        // expires and reports the correct end-of-month (9999-11-30) without throwing.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(
            null,
            "9999-11",
            null,
            null,
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = null,
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), noAuthorAccounts, noExemptions, noSeverityOverrides, noMessageOverrides, new TaskLoggingHelperFor(engine), new(9999, 12, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
        await Assert.That(engine.Errors[0].Message).Contains("9999-11-30");
    }

    // A lazy whose factory throws if forced — used to prove the happy paths never read the
    // diagnostic-only sidecars (authorAccounts, exemptions, severity/message overrides).
    static Lazy<T> Poison<T>() =>
        new(() => throw new InvalidOperationException("sidecar forced on a passing build"));

    [Test]
    public async Task SponsorMatch_DoesNotForceSidecarReads()
    {
        // A matching sponsor is the common happy path: DecisionApplier must return true without
        // touching any diagnostic-only sidecar. Poison lazies would throw if any were forced.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicenseModeResolver.Resolve(
            null,
            null,
            null,
            null,
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = "alice",
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), Poison<IReadOnlyList<AuthorAccount>>(), Poison<IReadOnlyDictionary<string, ExemptionDefinition>>(), Poison<IReadOnlyDictionary<string, Severity>>(), Poison<IReadOnlyDictionary<string, string>>(), new TaskLoggingHelperFor(engine), DateTime.UtcNow);
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task ValidLicense_DoesNotForceSidecarReads()
    {
        // The other happy path: a still-valid SponsorshipLicensedUntil returns without rendering a
        // diagnostic, so no sidecar is forced. The month sits inside the one-year cap, paired with
        // a fixed clock so the test doesn't age out of that window.
        using var dir = new TempDirectory();
        var path = WriteHashes(dir, ("GitHubSponsors", "alice"));
        var decision = LicensedDecision("2026-12");
        var engine = new StubBuildEngine();
        var ok = DecisionApplier.Apply(decision, path, "", NonCpmContext(), Poison<IReadOnlyList<AuthorAccount>>(), Poison<IReadOnlyDictionary<string, ExemptionDefinition>>(), Poison<IReadOnlyDictionary<string, Severity>>(), Poison<IReadOnlyDictionary<string, string>>(), new TaskLoggingHelperFor(engine), new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
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
            ConsumerProjectPath = consumerProject,
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
    public async Task SC005_PromotedToError_BuildFails()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            SeverityOverridesPath = WriteOverrides(dir, ("SC005", Severity.Error))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Warnings).IsEmpty();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC005");
    }

    [Test]
    public async Task SC007_DowngradedToWarning_BuildPasses()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory",
            SeverityOverridesPath = WriteOverrides(dir, ("SC007", Severity.Warning))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC007");
    }

    [Test]
    public async Task SC009_DowngradedToMessage_BuildPasses()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "2000-01",
            SeverityOverridesPath = WriteOverrides(dir, ("SC009", Severity.Message))
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).IsEmpty();
        await Assert.That(engine.Messages.Any(_ => _.Code == "SC009")).IsTrue();
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
            ConsumerProjectPath = consumerProject,
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
        await Assert.That(message).DoesNotContain("requires license metadata applied to");
        await Assert.That(message).Contains("#sc001");
    }

    [Test]
    public async Task SC005_MessageOverride_ReplacesDefaultText()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC005", "You agreed not to free-ride."))
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
            ConsumerProjectPath = consumerProject,
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
        // SC001 message override is set, but the consumer trips SC007 — the SC001 override is
        // never read.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "mallory",
            MessageOverridesPath = WriteMessageOverrides(dir, ("SC001", "should not appear"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Assert.That(engine.Errors[0].Message!).DoesNotContain("should not appear");
    }

    [Test]
    public async Task NonOverrideableCode_IgnoresEntryInSidecar()
    {
        // Verifier is tolerant of sidecar entries for non-overrideable codes (SC003 here) —
        // bundler-side validation is the source of truth, so any sneaky entry is silently dropped.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "SeverityOverrides.txt");
        await File.WriteAllLinesAsync(path, ["SC003=warning"]);
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice",
            SeverityOverridesPath = path
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
    }

    // --- SponsorLandingUrl override: duplicates of every URL-bearing snapshot test ---
    //
    // When the package author sets SponsorLandingUrl on their SponsorCheck reference, the verifier
    // replaces every per-platform sponsor URL (github.com/sponsors/..., opencollective.com/...,
    // polar.sh/...) with the single author-supplied landing URL. The "Sponsor at" block also
    // collapses to one line because RenderSponsorAtBlock dedupes identical URLs.
    //
    // Each test here mirrors an existing snapshot test above but with WriteLandingUrl(dir) plumbed
    // into LandingUrlPath. The snapshot proves the rendered message no longer contains the
    // platform URLs and instead contains the override URL.

    const string overrideLandingUrl = "https://acme.example.com/sponsor";

    static string WriteLandingUrl(TempDirectory dir, string url = overrideLandingUrl)
    {
        var path = Path.Combine(dir, "LandingUrl.txt");
        File.WriteAllText(path, url);
        return path;
    }

    [Test]
    public async Task NoConfig_WithLandingUrl_FailsWithSC001()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains(overrideLandingUrl);
        await Assert.That(message).DoesNotContain("github.com/sponsors");
        await Verify(engine);
    }

    [Test]
    public async Task SC001_WithLandingUrl_MessageReplacesAllPlatformUrls()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains(overrideLandingUrl);
        await Assert.That(message).DoesNotContain("https://github.com/sponsors/acmecorp");
        await Assert.That(message).DoesNotContain("https://opencollective.com/acme-org");
        await Assert.That(message).DoesNotContain("https://polar.sh/acme");
        await Verify(engine);
    }

    [Test]
    public async Task SC005_WithLandingUrl_MessageReplacesPlatformUrl()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsTrue();
        var message = engine.Warnings[0].Message!;
        await Assert.That(message).Contains(overrideLandingUrl);
        await Assert.That(message).DoesNotContain("https://github.com/sponsors/acmecorp");
        await Verify(engine);
    }

    [Test]
    public async Task IgnoredTrue_WithLandingUrl_PassesWithSC005Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC005");
        await Verify(engine);
    }

    [Test]
    public async Task InvalidSponsor_WithLandingUrl_FailsWithSC007()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromRef = "mallory",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    [Test]
    public async Task InvalidSponsor_OnePlatformConfigured_WithLandingUrl_ShowsOverrideUrl()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromRef = "mallory",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains($"Sponsor at {overrideLandingUrl}");
        await Assert.That(message).DoesNotContain("github.com/sponsors");
        await Verify(engine);
    }

    [Test]
    public async Task InvalidSponsor_MultiplePlatformsConfigured_WithLandingUrl_CollapsesToOneSponsorAt()
    {
        // Three platforms, one landing URL: RenderSponsorAtBlock dedupes so the rendered SC007
        // message has a single "Sponsor at <url>" line instead of a three-bullet block.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromRef = "mallory",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains($"Sponsor at {overrideLandingUrl}");
        await Assert.That(message).DoesNotContain("Sponsor at:");
        await Verify(engine);
    }

    [Test]
    public async Task CpmInvalidSponsor_WithLandingUrl_FailsWithSC008()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromVer = "mallory",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC008");
        await Verify(engine);
    }

    [Test]
    public async Task CpmInvalidSponsor_MultiplePlatformsConfigured_WithLandingUrl_CollapsesToOneSponsorAt()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromVer = "mallory",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC008");
        await Verify(engine);
    }

    [Test]
    public async Task ExpiredLicense_WithLandingUrl_FailsWithSC009()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromRef = "2000-01",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
        await Verify(engine);
    }

    [Test]
    public async Task ExpiredLicense_MultiplePlatformsConfigured_WithLandingUrl_CollapsesToOneSponsorAt()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LicensedUntilFromRef = "2000-01",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC009");
        await Verify(engine);
    }

    [Test]
    public async Task CpmExpiredLicense_WithLandingUrl_FailsWithSC010()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromVer = "2000-01",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC010");
        await Verify(engine);
    }

    [Test]
    public async Task CpmExpiredLicense_MultiplePlatformsConfigured_WithLandingUrl_CollapsesToOneSponsorAt()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LicensedUntilFromVer = "2000-01",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC010");
        await Verify(engine);
    }

    [Test]
    public async Task CpmIgnored_MultiplePlatformsConfigured_WithLandingUrl_WarnsWithSC006()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            IgnoredFromVer = "true",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC006");
        await Verify(engine);
    }

    [Test]
    public async Task CpmNoConfig_WithLandingUrl_FailsWithSC002()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
        await Verify(engine);
    }

    [Test]
    public async Task CpmNoConfig_MultiplePlatformsConfigured_WithLandingUrl_FailsWithSC002()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC002");
        await Verify(engine);
    }

    [Test]
    public async Task MultiplePlatformAccounts_NoneMatch_WithLandingUrl_FailsWithSC007ListingAllAttempts()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"), ("OpenCollective", "acme-org"), ("Polar", "acme")),
            GitHubFromRef = "mallory",
            OpenCollectiveFromRef = "wrong-org",
            PolarFromRef = "wrong-handle",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    [Test]
    public async Task SponsorshipStartBeforePackDate_WithLandingUrl_StillEnforcesHash()
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-01",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    [Test]
    public async Task SponsorshipStartEqualsPackDate_WithLandingUrl_FallsThroughToHash()
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
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.2.3",
            SponsorHashListPath = hashes,
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            PackDatePath = packDate,
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "2026-04-15",
            LandingUrlPath = WriteLandingUrl(dir)
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC007");
        await Verify(engine);
    }

    // Owner mode: OwnerId is set and configuration arrives via the *FromRef parameters (the owner
    // template feeds global MSBuild properties through those). Placement (SC019/SC020) is skipped.

    [Test]
    public async Task OwnerMode_NoConfig_FailsWithSC021()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC021");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_ConflictingModes_FailsWithSC022()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC022");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_Ignored_PassesWithSC023Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC023");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_ValidSponsor_Passes()
    {
        using var dir = new TempDirectory();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = new StubBuildEngine(),
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice"), ("GitHubSponsors", "bob")),
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsTrue();
    }

    [Test]
    public async Task OwnerMode_InvalidSponsor_FailsWithSC024()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            GitHubFromRef = "mallory"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC024");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_ExpiredLicense_FailsWithSC025()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            LicensedUntilFromRef = "2000-01"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC025");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_BadLicenseFormat_FailsWithSC026()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            LicensedUntilFromRef = "not-a-date"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC026");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_BadSponsorshipStartFormat_FailsWithSC027()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "yesterday"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC027");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerMode_FutureSponsorshipStart_FailsWithSC028()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "MyOssLib",
            OwnerId = "acme",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            GitHubFromRef = "carol",
            SponsorshipStartFromRef = "9999-12-31"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC028");
        await Verify(engine);
    }

    [Test]
    public async Task Exemption_KnownName_PassesWithSC029Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir,
                ("Consulting", "Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work."),
                ("SmallRevenue", "Consumers under US$10,000 annual gross revenue are exempt.")),
            SponsorshipExemptionFromRef = "Consulting"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings).HasSingleItem();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC029");
        await Assert.That(engine.Warnings[0].Message!).Contains("Organizations that have engaged");
        await Verify(engine);
    }

    [Test]
    public async Task Exemption_UnknownName_FailsWithSC032()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir,
                ("Consulting", "Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work."),
                ("SmallRevenue", "Consumers under US$10,000 annual gross revenue are exempt.")),
            SponsorshipExemptionFromRef = "MadeUpName"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC032");
        var message = engine.Errors[0].Message!;
        await Assert.That(message).Contains("Consulting");
        await Assert.That(message).Contains("SmallRevenue");
        await Verify(engine);
    }

    [Test]
    public async Task Exemption_NoExemptionsSidecar_FailsWithSC032()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            SponsorshipExemptionFromRef = "Consulting"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC032");
        await Assert.That(engine.Errors[0].Message!).Contains("publisher has not defined any exemptions");
        await Verify(engine);
    }

    [Test]
    public async Task Exemption_EmptyString_TreatedAsUnset()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromRef = ""
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
    }

    [Test]
    public async Task Exemption_CaseInsensitiveLookup_SurfacesConsumerCasing()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromRef = "consulting"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC029");
        var message = engine.Warnings[0].Message!;
        await Assert.That(message).Contains("\"consulting\"");
        await Assert.That(message).Contains("Consulting carve-out.");
        await Verify(engine);
    }

    // --- Time-bounded exemptions (MaxTermMonths / SponsorshipExemptionUntil) ---
    //
    // These run through DecisionApplier rather than the task so the build clock is fixed: every
    // code below is decided against utcNow, and a rendered ceiling month baked into a snapshot
    // would otherwise change every month. May 2026 + a 6 month cap puts the ceiling at 2026-11.

    static readonly DateTime clock = new(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);

    static Lazy<IReadOnlyDictionary<string, ExemptionDefinition>> Exemptions(params (string name, string message, int? maxTermMonths)[] entries) =>
        new(() => entries.ToDictionary(
            _ => _.name,
            _ => new ExemptionDefinition(_.message, _.maxTermMonths),
            StringComparer.OrdinalIgnoreCase));

    static LicenseDecision ExemptDecision(string name, string? until = null) =>
        LicenseModeResolver.Resolve(
            null,
            null,
            name,
            until,
            new Dictionary<string, string?>
            {
                ["GitHubSponsors"] = null,
                ["OpenCollective"] = null,
                ["Polar"] = null
            },
            null,
            "MyOssLib");

    static bool ApplyExemption(
        StubBuildEngine engine,
        LicenseDecision decision,
        Lazy<IReadOnlyDictionary<string, ExemptionDefinition>> exemptions,
        ConsumerContext? context = null,
        DateTime? utcNow = null) =>
        DecisionApplier.Apply(
            decision,
            "",
            "",
            context ?? NonCpmContext(),
            noAuthorAccounts,
            exemptions,
            noSeverityOverrides,
            noMessageOverrides,
            new TaskLoggingHelperFor(engine),
            utcNow ?? clock);

    [Test]
    public async Task BoundedExemption_WithinCap_PassesAndSC029NamesTheEndMonth()
    {
        // The end month rides along in the warning: the CI audit trail should record not just
        // which carve-out was claimed but how long it was claimed for.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(
            engine,
            ExemptDecision("Consulting", "2026-11"),
            Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC029");
        await Assert.That(engine.Warnings[0].Message!).Contains("until 2026-11");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_ExactlyAtCap_Passes()
    {
        // The ceiling is inclusive — 6 months from May 2026 is November 2026, and that value stands.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-11"), Exemptions(("Consulting", "text", 6)));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task BoundedExemption_OneMonthPastCap_FailsWithSC044()
    {
        // Mirror of the boundary above: the next month over is the first rejected value.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-12"), Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC044");
        await Assert.That(engine.Errors[0].Message!).Contains("maximum 2026-11");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_CapCrossesYearBoundary()
    {
        // The ceiling is month arithmetic, not "same month next year" — November 2026 + 6 lands in
        // May 2027, so a value inside that window has to pass and the one past it has to fail.
        var november = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var definition = Exemptions(("Consulting", "text", 6));
        var inside = new StubBuildEngine();
        await Assert.That(ApplyExemption(inside, ExemptDecision("Consulting", "2027-05"), definition, utcNow: november)).IsTrue();

        var outside = new StubBuildEngine();
        await Assert.That(ApplyExemption(outside, ExemptDecision("Consulting", "2027-06"), definition, utcNow: november)).IsFalse();
        await Assert.That(outside.Errors[0].Message!).Contains("maximum 2027-05");
    }

    [Test]
    public async Task BoundedExemption_MissingUntil_FailsWithSC038()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting"), Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC038");
        await Assert.That(engine.Errors[0].Message!).Contains("6 months");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_MissingUntil_Cpm_FailsWithSC039()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting"), Exemptions(("Consulting", "Consulting carve-out.", 6)), CpmContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC039");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_MissingUntil_Owner_FailsWithSC040()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting"), Exemptions(("Consulting", "Consulting carve-out.", 6)), OwnerContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC040");
        await Assert.That(engine.Errors[0].Message!).Contains("acme_SponsorshipExemptionUntil");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_SingleMonthCap_RendersSingular()
    {
        var engine = new StubBuildEngine();
        ApplyExemption(engine, ExemptDecision("Consulting"), Exemptions(("Consulting", "text", 1)));
        await Assert.That(engine.Errors[0].Message!).Contains("to 1 month.");
        await Assert.That(engine.Errors[0].Message!).DoesNotContain("1 months");
    }

    [Test]
    [Arguments("2026")]
    [Arguments("2026-13")]
    [Arguments("2026-11-01")]
    [Arguments("next year")]
    public async Task BoundedExemption_BadUntilFormat_FailsWithSC041(string value)
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", value), Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC041");
        await Assert.That(engine.Errors[0].Message!).Contains("yyyy-MM");
    }

    [Test]
    public async Task BoundedExemption_BadUntilFormat_Cpm_FailsWithSC042()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "later"), Exemptions(("Consulting", "Consulting carve-out.", 6)), CpmContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC042");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_BadUntilFormat_Owner_FailsWithSC043()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "later"), Exemptions(("Consulting", "Consulting carve-out.", 6)), OwnerContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC043");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_PastCap_Cpm_FailsWithSC045()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-12"), Exemptions(("Consulting", "Consulting carve-out.", 6)), CpmContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC045");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_PastCap_Owner_FailsWithSC046()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-12"), Exemptions(("Consulting", "Consulting carve-out.", 6)), OwnerContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC046");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_Expired_FailsWithSC047()
    {
        // The forcing function the cap exists for: a claim nobody revisited stops the build.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-04"), Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC047");
        await Assert.That(engine.Errors[0].Message!).Contains("2026-04-30");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_Expired_Cpm_FailsWithSC048()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-04"), Exemptions(("Consulting", "Consulting carve-out.", 6)), CpmContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC048");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_Expired_Owner_FailsWithSC049()
    {
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-04"), Exemptions(("Consulting", "Consulting carve-out.", 6)), OwnerContext());
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC049");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_LastInstantOfNamedMonth_Passes()
    {
        // Month granularity, same as SponsorshipLicensedUntil: the named month is fully covered
        // right up to its final tick, and the first day of the next month is the cutoff.
        var lastTick = new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc);
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-05"), Exemptions(("Consulting", "text", 6)), utcNow: lastTick);
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task BoundedExemption_FirstInstantOfNextMonth_FailsWithSC047()
    {
        var firstTick = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-05"), Exemptions(("Consulting", "text", 6)), utcNow: firstTick);
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC047");
    }

    [Test]
    public async Task UncappedExemption_WithoutUntil_StillPasses()
    {
        // Regression guard on the pre-MaxTermMonths contract: an exemption the publisher did not
        // cap is claimable exactly as before, with no end month demanded.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting"), Exemptions(("Consulting", "Consulting carve-out.", null)));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC029");
        await Assert.That(engine.Warnings[0].Message!).DoesNotContain("until");
    }

    [Test]
    public async Task UncappedExemption_ConsumerSuppliedUntil_IsHonoured()
    {
        // A consumer may bound an uncapped exemption of their own accord. Nothing forces it, but
        // once written it means what it says — including expiring.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2026-04"), Exemptions(("Consulting", "Consulting carve-out.", null)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC047");
        // No publisher ceiling to name, so the remediation block can't offer a specific month.
        await Assert.That(engine.Errors[0].Message!).Contains("yyyy-MM");
        await Verify(engine);
    }

    [Test]
    public async Task UncappedExemption_DistantUntil_HasNoCeiling()
    {
        // Without a publisher cap there is nothing to exceed — SC044 can only fire for a capped
        // exemption, so a far-future self-imposed bound is simply a bound that hasn't lapsed.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("Consulting", "2099-12"), Exemptions(("Consulting", "text", null)));
        await Assert.That(ok).IsTrue();
        await Assert.That(engine.Errors).IsEmpty();
    }

    [Test]
    public async Task BoundedExemption_UnknownName_StillFailsWithSC032()
    {
        // Name resolution comes first: an unknown name can't be checked against a cap that only
        // the publisher's definition carries.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(engine, ExemptDecision("MadeUpName", "2026-11"), Exemptions(("Consulting", "Consulting carve-out.", 6)));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC032");
        // The list of available exemptions flags which of them come with an end-date requirement.
        await Assert.That(engine.Errors[0].Message!).Contains("time-bounded");
        await Verify(engine);
    }

    [Test]
    public async Task BoundedExemption_MaxTermMonthsAtCalendarExtreme_DoesNotOverflow()
    {
        // AddMonths on a DateTime would throw here; the ceiling is computed on calendar fields so
        // a build at the end of representable time still renders a message instead of crashing.
        var engine = new StubBuildEngine();
        var ok = ApplyExemption(
            engine,
            ExemptDecision("Consulting"),
            Exemptions(("Consulting", "text", 240)),
            utcNow: new(9999, 12, 15, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC038");
        await Assert.That(engine.Errors[0].Message!).Contains("10019-12");
    }

    [Test]
    public async Task Exemption_PlusSponsor_FailsWithSC003()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromRef = "Consulting",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
        await Assert.That(engine.Errors[0].Message!).Contains("SponsorshipExemption");
        await Verify(engine);
    }

    [Test]
    public async Task Exemption_MisplacedOnPackageVersion_NonCpm_FailsWithSC020()
    {
        // Non-CPM project but exemption lives on <PackageVersion> — placement check catches it.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromVer = "Consulting"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC020");
        await Assert.That(engine.Errors[0].Message!).Contains("SponsorshipExemption");
    }

    [Test]
    public async Task CpmExemption_KnownName_PassesWithSC030Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir,
                ("Consulting", "Consulting carve-out."),
                ("SmallRevenue", "Small-revenue carve-out.")),
            SponsorshipExemptionFromVer = "SmallRevenue"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC030");
        await Assert.That(engine.Warnings[0].Message!).Contains("Small-revenue carve-out.");
        await Verify(engine);
    }

    [Test]
    public async Task CpmExemption_UnknownName_FailsWithSC033()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            IsCpm = "true",
            ConsumerProjectPath = consumerProject,
            DirectoryPackagesPropsPath = directoryPackagesProps,
            PackageVersionFromVer = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromVer = "MadeUpName"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC033");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerModeExemption_KnownName_PassesWithSC031Warning()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            OwnerId = "papyrine",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir,
                ("Consulting", "Consulting carve-out."),
                ("SmallRevenue", "Small-revenue carve-out.")),
            SponsorshipExemptionFromRef = "Consulting"
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(engine.Warnings[0].Code).IsEqualTo("SC031");
        await Assert.That(engine.Warnings[0].Message!).Contains("papyrine_SponsorshipExemption");
        await Assert.That(engine.Warnings[0].Message!).Contains("Consulting carve-out.");
        await Verify(engine);
    }

    [Test]
    public async Task OwnerModeExemption_UnknownName_FailsWithSC034()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            OwnerId = "papyrine",
            ConsumerProjectPath = consumerProject,
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            SponsorshipExemptionFromRef = "MadeUpName"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC034");
        await Assert.That(engine.Errors[0].Message!).Contains("papyrine_SponsorshipExemption");
        await Verify(engine);
    }

    [Test]
    public async Task SC001_WithExemptionsDefined_BodyIncludesExemptionOption()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out."))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
        await Assert.That(engine.Errors[0].Message!).Contains("Claim a publisher-defined exemption");
        await Assert.That(engine.Errors[0].Message!).Contains("SponsorshipExemption=\"Consulting\"");
    }

    [Test]
    public async Task SC001_WithoutExemptionsDefined_BodyOmitsExemptionOption()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp"))
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC001");
        await Assert.That(engine.Errors[0].Message!).DoesNotContain("Claim a publisher-defined exemption");
    }

    [Test]
    public async Task SC003_WithExemptionsDefined_KeepOneOfIncludesExemption()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            ExemptionsPath = WriteExemptions(dir, ("Consulting", "Consulting carve-out.")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
        await Assert.That(engine.Errors[0].Message!).Contains("SponsorshipExemption");
    }

    [Test]
    public async Task SC003_WithoutExemptionsDefined_KeepOneOfOmitsExemption()
    {
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new VerifySponsorshipTask
        {
            BuildEngine = engine,
            ThePackageId = "Papyrine",
            ConsumerProjectPath = consumerProject,
            PackageVersionFromRef = "1.0.0",
            SponsorHashListPath = WriteHashes(dir, ("GitHubSponsors", "alice")),
            AuthorAccountsPath = WriteAuthorAccounts(dir, ("GitHubSponsors", "acmecorp")),
            IgnoredFromRef = "true",
            GitHubFromRef = "alice"
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC003");
        await Assert.That(engine.Errors[0].Message!).DoesNotContain("SponsorshipExemption");
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
