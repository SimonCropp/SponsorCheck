namespace SponsorCheck.IntegrationTests;

public class ConsumerBuildTests
{
    static async Task<CliResult> BuildFixture(string fixtureName, string configuration = "Release", string authorFixture = "ThePackage")
    {
        var feed = await ThePackageBuilder.EnsureBuilt(authorFixture);
        return await BuildFixtureInFeed(fixtureName, feed, configuration);
    }

    static async Task<CliResult> BuildFixtureInFeed(string fixtureName, string feed, string configuration = "Release")
    {
        var workDir = TestEnvironment.MakeWorkDir(fixtureName);
        TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDir, fixtureName), workDir);
        TestEnvironment.WriteNugetConfig(workDir, feed);
        // Empty Directory.Build.props/targets so the temp dir doesn't pick up parent IntegrationTests
        // config — but only when the fixture didn't ship its own. Owner-mode fixtures put their
        // sponsorship property in Directory.Build.props, which must survive.
        var directoryBuildProps = Path.Combine(workDir, "Directory.Build.props");
        if (!File.Exists(directoryBuildProps))
        {
            File.WriteAllText(directoryBuildProps, "<Project/>");
        }

        var directoryBuildTargets = Path.Combine(workDir, "Directory.Build.targets");
        if (!File.Exists(directoryBuildTargets))
        {
            File.WriteAllText(directoryBuildTargets, "<Project/>");
        }

        var packagesDir = Path.Combine(workDir, ".pkgs");
        Directory.CreateDirectory(packagesDir);
        var project = Directory.GetFiles(workDir)
            .Single(f => f.EndsWith(".csproj") || f.EndsWith(".fsproj") || f.EndsWith(".vbproj"));
        return await DotnetCliRunner.Run("build", project, configuration, null, workDir, packagesDir);
    }

    [Test]
    public async Task ValidGitHubSponsor_BuildsCleanly()
    {
        var result = await BuildFixture("Consumer.ValidGitHubSponsor");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }

    [Test]
    public async Task InvalidSponsor_FailsWithSC007()
    {
        var result = await BuildFixture("Consumer.InvalidSponsor");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC007");
    }

    [Test]
    public async Task IgnoredLicense_BuildsWithSC005Warning()
    {
        var result = await BuildFixture("Consumer.IgnoredLicense");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC005");
    }

    [Test]
    public async Task NoConfig_FailsWithSC001()
    {
        var result = await BuildFixture("Consumer.NoConfig");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
    }

    [Test]
    public async Task FutureLicense_BuildsCleanly()
    {
        var result = await BuildFixture("Consumer.FutureLicense");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }

    [Test]
    public async Task ExpiredLicense_FailsWithSC009()
    {
        var result = await BuildFixture("Consumer.ExpiredLicense");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC009");
    }

    [Test]
    public async Task MultipleModes_FailsWithSC003()
    {
        var result = await BuildFixture("Consumer.MultipleModes");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC003");
    }

    [Test]
    public async Task AnyMatchPasses_OnePlatformIsEnough()
    {
        var result = await BuildFixture("Consumer.AnyMatchPasses");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }

    [Test]
    public async Task DebugBuild_StillEnforcesSponsorship()
    {
        // The verifier runs in every configuration, not just Release. Debug builds without a
        // license-mode declaration must still fail with SC001 — same shape as Release.
        var result = await BuildFixture("Consumer.DebugEnforced", "Debug");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
    }

    [Test]
    public async Task FSharpConsumer_BuildsCleanly()
    {
        var result = await BuildFixture("Consumer.FSharp");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }

    [Test]
    public async Task VbConsumer_BuildsCleanly()
    {
        var result = await BuildFixture("Consumer.VB");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }

    [Test]
    public async Task RecentSponsor_TrustedDespiteNotInBundledList()
    {
        var result = await BuildFixture("Consumer.RecentSponsor");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("trusting unverified sponsor");
    }

    [Test]
    public async Task CpmConsumer_LicenseMetadataOnPackageVersion_PassesWithoutBatchingError()
    {
        // Regression: under CPM the consumer's license metadata lives on <PackageVersion>, not
        // <PackageReference>. Without the property-flatten in ConsumerVerifier.targets, MSBuild
        // task-batches the verifier across the two ItemGroups. The PackageReference batch (no
        // metadata) trips SC001 (no license mode), even though the PackageVersion batch succeeds
        // with SponsorshipLicenseIgnored=true.
        var result = await BuildFixture("Consumer.Cpm");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        // CPM consumers emit the SC2xx sibling of each diagnostic. SC006 is the CPM "license ignored" warning.
        await Assert.That(result.Combined).Contains("SC006");
        await Assert.That(result.Combined).DoesNotContain("SC001");
        await Assert.That(result.Combined).DoesNotContain("SC002");
        await Assert.That(result.Combined).DoesNotContain("SC003");
        await Assert.That(result.Combined).DoesNotContain("SC004");
    }

    [Test]
    public async Task CpmConsumer_LicenseMetadataOnPackageReference_FailsWithSC020()
    {
        // CPM is on but the consumer wrongly put SponsorCheck metadata on <PackageReference>
        // instead of <PackageVersion>. The verifier must reject the placement with SC020 before
        // it ever reaches the license-mode check (which would otherwise fire SC001).
        var result = await BuildFixture("Consumer.CpmMisplaced");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC020");
        await Assert.That(result.Combined).DoesNotContain("SC001");
    }

    [Test]
    public async Task CpmNoConfig_FailsWithSC002()
    {
        // CPM sibling of SC001 (Consumer.NoConfig). License metadata is missing on the
        // PackageVersion; the verifier must fire SC002, not SC001.
        var result = await BuildFixture("Consumer.CpmNoConfig");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC002");
        await Assert.That(result.Combined).DoesNotContain("SC001");
    }

    [Test]
    public async Task CpmMultipleModes_FailsWithSC004()
    {
        // CPM sibling of SC003 (Consumer.MultipleModes). Two mutually-exclusive license modes
        // declared on the PackageVersion must trip SC004, not SC003.
        var result = await BuildFixture("Consumer.CpmMultipleModes");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC004");
        await Assert.That(result.Combined).DoesNotContain("SC003");
    }

    [Test]
    public async Task CpmInvalidSponsor_FailsWithSC008()
    {
        // CPM sibling of SC007 (Consumer.InvalidSponsor). A sponsor account on PackageVersion
        // that doesn't match the bundled hash list must trip SC008, not SC007.
        var result = await BuildFixture("Consumer.CpmInvalidSponsor");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC008");
        await Assert.That(result.Combined).DoesNotContain("SC007");
    }

    [Test]
    public async Task CpmExpiredLicense_FailsWithSC010()
    {
        // CPM sibling of SC009 (Consumer.ExpiredLicense). Expired SponsorshipLicensedUntil on
        // the PackageVersion must trip SC010, not SC009.
        var result = await BuildFixture("Consumer.CpmExpiredLicense");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC010");
        await Assert.That(result.Combined).DoesNotContain("SC009");
    }

    [Test]
    public async Task FutureSponsorshipStart_FailsWithSC015()
    {
        var result = await BuildFixture("Consumer.FutureSponsorshipStart");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC015");
    }

    [Test]
    public async Task SeverityOverride_SC001DowngradedToWarning_BuildsCleanly()
    {
        // Author bakes NoLicenseSpecifiedSeverityOverride="warning". A consumer that omits all
        // license-mode metadata would normally fail with SC001 — here the build should succeed
        // because the sidecar shipped in the nupkg flips the severity to warning.
        var result = await BuildFixture(
            "Consumer.OverrideSC001NoConfig",
            authorFixture: "ThePackageOverridden");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
        await Assert.That(result.Combined).DoesNotContain("error SC001");
    }

    [Test]
    public async Task MessageOverride_SC001_CustomMessageReachesBuildLog()
    {
        // ThePackageOverridden also bakes NoLicenseSpecifiedMessageOverride="Please sponsor...".
        // The consumer build should show the custom text, not the default "requires one
        // license-mode metadata" boilerplate.
        var result = await BuildFixture(
            "Consumer.OverrideSC001NoConfig",
            authorFixture: "ThePackageOverridden");
        await Assert.That(result.Combined).Contains("Please sponsor ThePackageOverridden before using.");
        await Assert.That(result.Combined).DoesNotContain("requires license metadata applied to");
    }

    [Test]
    public async Task SeverityOverride_SC005PromotedToError_BuildFails()
    {
        // Author bakes LicenseIgnoredSeverityOverride="error". A consumer setting
        // SponsorshipLicenseIgnored="true" — which would normally pass with a warning — must
        // now fail because the override hardens the rule.
        var result = await BuildFixture(
            "Consumer.OverrideSC005Ignored",
            authorFixture: "ThePackageOverridden");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("error SC005");
    }

    [Test]
    public async Task MessageOverride_SC005_CustomMessageReachesBuildLog()
    {
        var result = await BuildFixture(
            "Consumer.OverrideSC005Ignored",
            authorFixture: "ThePackageOverridden");
        await Assert.That(result.Combined).Contains("You agreed not to free-ride this library.");
        await Assert.That(result.Combined).DoesNotContain("Build is allowed but is in breach");
    }

    [Test]
    public async Task OwnerMode_PropertyInDirectoryBuildProps_BuildsCleanly()
    {
        // Owner mode: the author published ThePackageOwnerMode with SponsorOwner="acme". The consumer
        // declares its sponsor account once as a global property — here in Directory.Build.props,
        // which the fixture ships and BuildFixture must not clobber. 'alice' is in the bundled list.
        var result = await BuildFixture("Consumer.OwnerDirectoryBuildProps", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
        await Assert.That(result.Combined).DoesNotContain("SC02");
    }

    [Test]
    public async Task OwnerMode_PropertyInCsproj_BuildsCleanly()
    {
        // Same as above but the global property is set directly in the consuming csproj.
        var result = await BuildFixture("Consumer.OwnerCsprojProperty", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
        await Assert.That(result.Combined).DoesNotContain("SC02");
    }

    [Test]
    public async Task OwnerMode_NoConfig_FailsWithSC021()
    {
        // Owner-mode counterpart of SC001/SC002: no sponsorship property set anywhere.
        var result = await BuildFixture("Consumer.OwnerNoConfig", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC021");
    }

    [Test]
    public async Task OwnerMode_InvalidSponsor_FailsWithSC024()
    {
        // Owner-mode counterpart of SC007/SC008: the property names an account not in the bundled list.
        var result = await BuildFixture("Consumer.OwnerInvalidSponsor", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC024");
    }

    [Test]
    public async Task OwnerMode_Ignored_BuildsWithSC023Warning()
    {
        // Owner-mode counterpart of SC005/SC006: SponsorshipLicenseIgnored property opts out.
        var result = await BuildFixture("Consumer.OwnerIgnored", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC023");
    }

    [Test]
    public async Task OwnerMode_FutureLicense_BuildsCleanly()
    {
        // Owner-mode counterpart of Consumer.FutureLicense: a time-bounded private license declared as
        // the global SponsorshipLicensedUntil property, valid through a future month, builds silently.
        var result = await BuildFixture("Consumer.OwnerFutureLicense", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
        await Assert.That(result.Combined).DoesNotContain("SC02");
    }

    [Test]
    public async Task OwnerMode_ExpiredLicense_FailsWithSC025()
    {
        // Owner-mode counterpart of SC009/SC010 (Consumer.ExpiredLicense / Consumer.CpmExpiredLicense):
        // an expired SponsorshipLicensedUntil global property must trip SC025, not SC009.
        var result = await BuildFixture("Consumer.OwnerExpiredLicense", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC025");
        await Assert.That(result.Combined).DoesNotContain("SC009");
    }

    [Test]
    public async Task OwnerMode_LeftoverPackageReferenceMetadata_StillFailsWithSC021()
    {
        // Transition per-package -> owner. The package is now owner mode but the consumer left the
        // sponsor account as <PackageReference> metadata (the per-package way) and set no global
        // property. Owner mode reads the property only: the leftover metadata is ignored (so no
        // SC020 placement error fires), and the build fails with SC021 directing them to the property.
        var result = await BuildFixture("Consumer.OwnerLeftoverMetadata", authorFixture: "ThePackageOwnerMode");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC021");
        await Assert.That(result.Combined).DoesNotContain("SC020");
    }

    [Test]
    public async Task PerPackageMode_StrayGlobalProperty_FailsWithSC001()
    {
        // Transition owner -> per-package. ThePackage is per-package mode but the consumer left the
        // sponsor account as a global property (the owner-mode way) and set no <PackageReference>
        // metadata. Per-package mode reads item metadata only, so the property is ignored -> SC001.
        var result = await BuildFixture("Consumer.PerPackageStrayProperty");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
    }

    [Test]
    public async Task OwnerAndPerPackage_BothConfigured_MixedFleetBuildsCleanly()
    {
        // Backs the README "set both during transition" guidance. One project references an owner-mode
        // package (ThePackageOwnerMode) and a per-package package (ThePackage), with the sponsor
        // account declared BOTH as a global property (read by the owner-mode package) and as
        // <PackageReference> metadata (read by the per-package package). The two MSBuild sources don't
        // interfere, so every package verifies and the build is clean.
        var feed = await ThePackageBuilder.EnsureBuiltCombined("ThePackage", "ThePackageOwnerMode");
        var result = await BuildFixtureInFeed("Consumer.OwnerMixedFleet", feed);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
        await Assert.That(result.Combined).DoesNotContain("SC02");
    }

    [Test]
    public async Task LandingUrlOverride_SC001_BuildLogContainsLandingUrlNotPlatformUrls()
    {
        // ThePackageLandingUrl bakes SponsorLandingUrl="https://acme.example.com/sponsor" on its
        // SponsorCheck reference. A consumer that omits license metadata trips SC001; the rendered
        // message must point at the author's landing URL instead of github.com/sponsors etc.
        var result = await BuildFixture(
            "Consumer.LandingUrlNoConfig",
            authorFixture: "ThePackageLandingUrl");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
        await Assert.That(result.Combined).Contains("https://acme.example.com/sponsor");
        await Assert.That(result.Combined).DoesNotContain("https://github.com/sponsors/acmecorp");
        await Assert.That(result.Combined).DoesNotContain("https://opencollective.com/acme-org");
        await Assert.That(result.Combined).DoesNotContain("https://polar.sh/acme");
    }

    [Test]
    public async Task TransitiveReference_WhenCheckTransitiveEnabled_IsVerified()
    {
        // ThePackageTransitive sets CheckTransitiveReferences="true", so its verifier ships under
        // buildTransitive/ and flows through MiddlePackageTransitive to the consumer. The consumer
        // references ThePackageTransitive only transitively (no <PackageReference> of its own) and
        // declares no sponsor account, so the transitively-imported verifier fails the build with SC001.
        var feed = await ThePackageBuilder.EnsureBuiltCombined("ThePackageTransitive", "MiddlePackageTransitive");
        var result = await BuildFixtureInFeed("Consumer.TransitiveChecked", feed);
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC001");
    }

    [Test]
    public async Task TransitiveReference_ByDefault_IsNotVerified()
    {
        // The default ThePackage (no CheckTransitiveReferences) keeps its verifier under build/, which
        // NuGet imports only for direct references. MiddlePackageDefault references ThePackage directly
        // (and verifies fine at its own pack time); the consumer pulls ThePackage in only transitively,
        // so the verifier never runs there and the build is clean despite declaring no sponsor account.
        var feed = await ThePackageBuilder.EnsureBuiltCombined("ThePackage", "MiddlePackageDefault");
        var result = await BuildFixtureInFeed("Consumer.TransitiveNotChecked", feed);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).DoesNotContain("SC00");
    }
}
