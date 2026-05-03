namespace SponsorCheck.IntegrationTests;

public class ConsumerBuildTests
{
    static async Task<CliResult> BuildFixture(string fixtureName, string configuration = "Release")
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var workDir = TestEnvironment.MakeWorkDir(fixtureName);
        TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDir, fixtureName), workDir);
        TestEnvironment.WriteNugetConfig(workDir, feed);
        // Empty Directory.Build.props/targets so the temp dir doesn't pick up parent IntegrationTests config.
        File.WriteAllText(Path.Combine(workDir, "Directory.Build.props"), "<Project/>");
        File.WriteAllText(Path.Combine(workDir, "Directory.Build.targets"), "<Project/>");

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
    public async Task InvalidSponsor_FailsWithSC004()
    {
        var result = await BuildFixture("Consumer.InvalidSponsor");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC004");
    }

    [Test]
    public async Task IgnoredLicense_BuildsWithSC003Warning()
    {
        var result = await BuildFixture("Consumer.IgnoredLicense");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC003");
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
    public async Task ExpiredLicense_FailsWithSC005()
    {
        var result = await BuildFixture("Consumer.ExpiredLicense");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC005");
    }

    [Test]
    public async Task MultipleModes_FailsWithSC002()
    {
        var result = await BuildFixture("Consumer.MultipleModes");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC002");
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
        await Assert.That(result.Combined).Contains("SC003");
        await Assert.That(result.Combined).DoesNotContain("SC001");
        await Assert.That(result.Combined).DoesNotContain("SC002");
    }

    [Test]
    public async Task FutureSponsorshipStart_FailsWithSC014()
    {
        var result = await BuildFixture("Consumer.FutureSponsorshipStart");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC014");
    }
}
