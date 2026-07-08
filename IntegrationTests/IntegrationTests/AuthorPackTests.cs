namespace SponsorCheck.IntegrationTests;

using System.IO.Compression;

public class AuthorPackTests
{
    [Test]
    public async Task ProducedNupkgContainsBuildAndTasksFolders()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackage.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/SponsorCheck.dll"))).IsTrue();
        await Assert.That(entries.Any(e => e.StartsWith("tasks/net472/SponsorCheck.dll"))).IsTrue();
        // Default (no CheckTransitiveReferences): everything stays under build/, so NuGet only imports
        // the verifier for direct references. Nothing leaks into buildTransitive/.
        await Assert.That(entries.Any(e => e.StartsWith("buildTransitive/"))).IsFalse();
    }

    [Test]
    public async Task CheckTransitiveReferences_PacksVerifierIntoBuildTransitive()
    {
        // ThePackageTransitive sets CheckTransitiveReferences="true". The bundler must ship the
        // generated verifier and its sidecars under buildTransitive/ (imported for direct *and*
        // transitive references) instead of build/ (direct only). The tasks/ DLLs are unaffected.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageTransitive");
        var nupkg = Directory.GetFiles(feed, "ThePackageTransitive.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("buildTransitive/ThePackageTransitive.targets");
        await Assert.That(entries).Contains("buildTransitive/SponsorCheck.SponsorHashes.txt");
        await Assert.That(entries).Contains("buildTransitive/SponsorCheck.PackDate.txt");
        await Assert.That(entries).Contains("buildTransitive/SponsorCheck.AuthorAccounts.txt");
        // The build/ folder must be empty of the verifier — otherwise a direct reference would import
        // it twice (build/ + buildTransitive/) and the data sidecars would be split across folders.
        await Assert.That(entries.Any(e => e.StartsWith("build/"))).IsFalse();
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/SponsorCheck.dll"))).IsTrue();
    }

    [Test]
    public async Task OwnTargets_RelocatedToSidecar_NoCollision()
    {
        // ThePackageOwnTargets ships its own <PackageId>.targets into build/ AND buildTransitive/ while
        // also setting CheckTransitiveReferences. Both the author file and the generated verifier claim
        // the buildTransitive/<id>.targets auto-import slot. The bundler must relocate the author's file
        // to <id>.SponsorCheckInner.targets and point the verifier's <Import> at it — so the verifier
        // owns the slot and the author's logic still loads — instead of NU5118 / a dropped verifier.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOwnTargets");
        var nupkg = Directory.GetFiles(feed, "ThePackageOwnTargets.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Verifier owns the buildTransitive auto-import slot...
        await Assert.That(entries).Contains("buildTransitive/ThePackageOwnTargets.targets");
        // ...the author's own targets were relocated to the sidecar the verifier imports...
        await Assert.That(entries).Contains("buildTransitive/ThePackageOwnTargets.SponsorCheckInner.targets");
        // ...and the build/ copy (imported by NuGet for direct references) is left in place.
        await Assert.That(entries).Contains("build/ThePackageOwnTargets.targets");

        var verifier = await ReadEntry(zip, "buildTransitive/ThePackageOwnTargets.targets");
        await Assert.That(verifier).Contains("VerifySponsorshipTask");
        await Assert.That(verifier).Contains("ThePackageOwnTargets.SponsorCheckInner.targets");

        var inner = await ReadEntry(zip, "buildTransitive/ThePackageOwnTargets.SponsorCheckInner.targets");
        await Assert.That(inner).Contains("ThePackageOwnTargets_AuthorMarker");
        await Assert.That(inner).DoesNotContain("VerifySponsorshipTask");

        // The build/ copy must still be the author's content, not the verifier.
        var buildCopy = await ReadEntry(zip, "build/ThePackageOwnTargets.targets");
        await Assert.That(buildCopy).Contains("ThePackageOwnTargets_AuthorMarker");
        await Assert.That(buildCopy).DoesNotContain("VerifySponsorshipTask");
    }

    static async Task<string> ReadEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Test]
    public async Task OwnTargets_RelocatedToSidecar_Cpm()
    {
        // Regression for an MSBuild Linux quirk: the previous detection property used
        // '$(BuildFolder)\$(PackageId).targets' to build the colliding-PackagePath comparison string.
        // MSBuild on Linux normalises raw '\' between two $(..) references to '/' inside expression
        // results, so the comparison string collapsed to forward-slash and never matched the author's
        // backslash-bearing PackagePath metadata (item metadata IS preserved verbatim on Linux). The
        // relocation silently skipped, the verifier and author's file both claimed the buildTransitive
        // slot, and pack failed with NU5118. The fix is %5C in the property definition, which decodes
        // to literal '\' on every platform and survives normalisation. This fixture exercises that exact
        // shape: CPM with CheckTransitiveReferences on PackageVersion, multi-targeting, and the author
        // packing their own <PackageId>.targets to both build/ and buildTransitive/.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOwnTargetsCpm");
        var nupkg = Directory.GetFiles(feed, "ThePackageOwnTargetsCpm.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(_ => _.FullName).ToList();

        await Assert.That(entries).Contains("buildTransitive/ThePackageOwnTargetsCpm.targets");
        await Assert.That(entries).Contains("buildTransitive/ThePackageOwnTargetsCpm.SponsorCheckInner.targets");
        await Assert.That(entries).Contains("build/ThePackageOwnTargetsCpm.targets");

        var verifier = await ReadEntry(zip, "buildTransitive/ThePackageOwnTargetsCpm.targets");
        await Assert.That(verifier).Contains("VerifySponsorshipTask");
        await Assert.That(verifier).Contains("ThePackageOwnTargetsCpm.SponsorCheckInner.targets");

        var inner = await ReadEntry(zip, "buildTransitive/ThePackageOwnTargetsCpm.SponsorCheckInner.targets");
        await Assert.That(inner).Contains("ThePackageOwnTargetsCpm_AuthorMarker");
        await Assert.That(inner).DoesNotContain("VerifySponsorshipTask");
    }

    [Test]
    public async Task BundledHashesMatchOverrideListAndAreDeterministic()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SponsorHashes.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        // Override list has 4 entries: alice, bob (GitHub), acme-org (OC), acme (Polar)
        await Assert.That(lines.Length).IsEqualTo(4);
        for (var i = 1; i < lines.Length; i++)
        {
            await Assert.That(string.CompareOrdinal(lines[i - 1], lines[i])).IsLessThan(0).Because("output should be sorted ordinal");
        }
    }

    [Test]
    public async Task MultiTargeted_ProducedNupkgContainsBundlerOutputs()
    {
        // Multi-targeted authors hit a different MSBuild import path: NuGet's per-TFM <project>.nuget.g.targets
        // ImportGroups don't fire in the outer multi-target build (where Pack runs). buildMultiTargeting/SponsorCheck.targets
        // is what makes the bundler visible to that outer build.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageMulti");
        var nupkg = Directory.GetFiles(feed, "ThePackageMulti.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackageMulti.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
        await Assert.That(entries.Any(e => e.StartsWith("tasks/netstandard2.0/SponsorCheck.dll"))).IsTrue();
    }

    [Test]
    public async Task CpmMultiTargeted_MetadataOnPackageVersion_BundlesSuccessfully()
    {
        // Regression: when SponsorCheck metadata lives on <PackageVersion> (CPM) and the author project
        // multi-targets, MSBuild *task batches* on `%(ItemGroup.Metadata)` parameters when two ItemGroups
        // are in scope (PackageReference batch + PackageVersion batch). The PackageReference batch under
        // CPM has no SponsorCheck metadata, so that invocation tripped SC101 even though the PackageVersion
        // batch succeeded. Fix: SponsorCheck.targets flattens metadata into scalar properties first
        // (`@(Items->'%(M)')`), which kills the batching. Same fix in ConsumerVerifier.targets — see
        // ConsumerBuildTests.CpmConsumer_LicenseMetadataOnPackageVersion_PassesWithoutBatchingError.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageCpm");
        var nupkg = Directory.GetFiles(feed, "ThePackageCpm.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackageCpm.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
    }

    [Test]
    public async Task OwnerMode_GeneratesOwnerVerifierTargets()
    {
        // ThePackageOwnerMode sets SponsorOwner="acme", so the bundler must emit the owner template
        // (reads global MSBuild properties + bakes in the owner id) rather than the per-package
        // template. The owner-mode license check pulls from $(GitHubSponsorAccount) etc., not from
        // per-PackageReference metadata. The template DOES still query @(PackageReference) for the
        // project-reference coverage responders — that's a separate concern (skip transitive
        // verification when a sibling ProjectReference covers the package directly).
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOwnerMode");
        var nupkg = Directory.GetFiles(feed, "ThePackageOwnerMode.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/ThePackageOwnerMode.targets")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("VerifySponsorshipTask");
        await Assert.That(content).Contains("$(GitHubSponsorAccount)");
        await Assert.That(content).Contains("_SponsorCheck_OwnerId>acme<");
        // The license-check task call must not pull from per-package metadata under owner mode.
        await Assert.That(content).DoesNotContain("GitHubFromVer");
        await Assert.That(content).DoesNotContain("IgnoredFromVer");
    }

    [Test]
    public async Task BundledTargetsReferencesRightAssembly()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/ThePackage.targets")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("VerifySponsorshipTask");
        await Assert.That(content).Contains("_SponsorCheck_Verify_ThePackage");
    }

    [Test]
    public async Task SeverityOverrides_BundledIntoNupkg()
    {
        // ThePackageOverridden declares NoLicenseSpecifiedSeverityOverride="warning" and
        // LicenseIgnoredSeverityOverride="error" on its SponsorCheck reference. The bundler
        // should write the resolved pairs to build/SponsorCheck.SeverityOverrides.txt — that's
        // what the verifier reads at consumer build time.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOverridden");
        var nupkg = Directory.GetFiles(feed, "ThePackageOverridden.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SeverityOverrides.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        // Each override metadatum applies to the per-package code, its CPM sibling, and its owner-mode
        // sibling, so the bundled file carries three entries per author-supplied override.
        string[] expected = ["SC001=warning", "SC002=warning", "SC021=warning", "SC005=error", "SC006=error", "SC023=error"];
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines).IsEquivalentTo(expected);
    }

    [Test]
    public async Task MessageOverrides_BundledIntoNupkg()
    {
        // ThePackageOverridden also declares NoLicenseSpecified/LicenseIgnoredMessageOverride —
        // the JSON sidecar must contain the verbatim author-supplied strings.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOverridden");
        var nupkg = Directory.GetFiles(feed, "ThePackageOverridden.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.MessageOverrides.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("Please sponsor ThePackageOverridden before using.");
        await Assert.That(content).Contains("You agreed not to free-ride this library.");
    }

    [Test]
    public async Task SeverityOverrides_AbsentOnUnoverriddenPackage()
    {
        // The bundler always writes the sidecar (for determinism/path resolution) but it's empty
        // when no override metadata is declared.
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.SeverityOverrides.txt")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content.Trim()).IsEqualTo("");
    }

    [Test]
    public async Task MessageOverrides_EmptyJsonOnUnoverriddenPackage()
    {
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.MessageOverrides.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task NoPlatformAccount_FailsPackWithSC101()
    {
        // ThePackageNoPlatform references SponsorCheck without any <Platform>Account metadata.
        // The bundler must fail-fast with SC101 before any platform fetch is attempted.
        var result = await ThePackageBuilder.TryPack("ThePackageNoPlatform");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC101");
        await Assert.That(result.Combined).Contains("at least one platform account metadata");
    }

    [Test]
    public async Task MissingCredential_FailsPackWithSC102()
    {
        // ThePackageMissingCredential declares GitHubSponsorsAccount but supplies no token.
        // Pack without the override list so the real platform fetch is attempted, and force
        // GitHubToken="" so any ambient env-var doesn't satisfy the credential requirement.
        var result = await ThePackageBuilder.TryPack(
            "ThePackageMissingCredential",
            useOverrideList: false,
            extraProperties: new Dictionary<string, string>
            {
                ["GitHubToken"] = ""
            });
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC102");
        await Assert.That(result.Combined).Contains("GitHub Sponsors: API token required");
    }

    [Test]
    public async Task PullRequestBuild_SkipsBundling()
    {
        // On PR CI the platform credential is normally unavailable, so rather than failing SC102 the
        // bundler is skipped and the package packs cleanly without the verifier. Simulate a PR via
        // the AppVeyor signal — MSBuild reads the env-var-named property directly.
        // forceBundleInPullRequest:false so the suite's hermeticity guard doesn't re-enable bundling.
        var (result, feed) = await ThePackageBuilder.TryPackToFeed(
            "ThePackage",
            extraProperties: new Dictionary<string, string>
            {
                ["APPVEYOR_PULL_REQUEST_NUMBER"] = "7"
            },
            forceBundleInPullRequest: false);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("skipping sponsor-list bundling");

        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(_ => _.FullName).ToList();
        // No verifier, no bundled sponsor data, no tasks/ DLLs — all live inside the skipped target.
        await Assert.That(entries.Any(_ => _ == "build/ThePackage.targets")).IsFalse();
        await Assert.That(entries.Any(_ => _.StartsWith("build/SponsorCheck."))).IsFalse();
        await Assert.That(entries.Any(_ => _.StartsWith("tasks/"))).IsFalse();
    }

    [Test]
    public async Task PullRequestBuild_OverrideForcesBundling()
    {
        // <SponsorCheckBundleInPullRequest>true</> opts back in: the bundler runs even on a PR, so
        // the verifier and bundled sponsor data are present exactly as on a normal build.
        var (result, feed) = await ThePackageBuilder.TryPackToFeed(
            "ThePackage",
            extraProperties: new Dictionary<string, string>
            {
                ["APPVEYOR_PULL_REQUEST_NUMBER"] = "7"
            },
            forceBundleInPullRequest: true);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Combined);

        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(_ => _.FullName).ToList();
        await Assert.That(entries).Contains("build/ThePackage.targets");
        await Assert.That(entries).Contains("build/SponsorCheck.SponsorHashes.txt");
    }

    [Test]
    public async Task SeverityOverrides_InvalidValue_FailsPackWithSC104()
    {
        // ThePackageBadOverride declares NoLicenseSpecifiedSeverityOverride="critical" — not a
        // recognized severity. Pack should fail with SC104, naming the offending metadata.
        var result = await ThePackageBuilder.TryPack("ThePackageBadOverride");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC104");
        await Assert.That(result.Combined).Contains("NoLicenseSpecifiedSeverityOverride");
        await Assert.That(result.Combined).Contains("critical");
    }

    [Test]
    public async Task Exemptions_BundledIntoNupkg()
    {
        // ThePackageWithExemptions declares two <SponsorExemption> items. The bundler must write
        // them into build/SponsorCheck.Exemptions.json as JSON with the publisher's verbatim text.
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageWithExemptions");
        var nupkg = Directory.GetFiles(feed, "ThePackageWithExemptions.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.Exemptions.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("Consulting");
        await Assert.That(content).Contains("Organizations that have engaged");
        await Assert.That(content).Contains("SmallRevenue");
        await Assert.That(content).Contains("US$10,000");
    }

    [Test]
    public async Task Exemptions_OwnerMode_BundledIntoNupkg()
    {
        var feed = await ThePackageBuilder.EnsureBuilt("ThePackageOwnerModeWithExemptions");
        var nupkg = Directory.GetFiles(feed, "ThePackageOwnerModeWithExemptions.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.Exemptions.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains("Consulting");
        await Assert.That(content).Contains("SmallRevenue");
    }

    [Test]
    public async Task Exemptions_EmptyJsonOnPackageWithoutExemptions()
    {
        // The sidecar is always written (for deterministic packaging) — empty object when
        // no <SponsorExemption> items were declared.
        var feed = await ThePackageBuilder.EnsureBuilt();
        var nupkg = Directory.GetFiles(feed, "ThePackage.*.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        var entry = zip.GetEntry("build/SponsorCheck.Exemptions.json")!;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        await Assert.That(content.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task Exemptions_InvalidDefinition_FailsPackWithSC106()
    {
        // ThePackageBadExemption declares <SponsorExemption Include="Consulting" Message="" />
        // — empty Message must trip SC106 at pack time.
        var result = await ThePackageBuilder.TryPack("ThePackageBadExemption");
        await Assert.That(result.ExitCode).IsNotEqualTo(0).Because(result.Combined);
        await Assert.That(result.Combined).Contains("SC106");
        await Assert.That(result.Combined).Contains("Consulting");
    }
}
