namespace SponsorCheck.IntegrationTests;

/// One-time setup that packs an author-side fixture (e.g. _Shared/ThePackage, _Shared/ThePackageMulti)
/// into a per-suite local feed dir. Subsequent consumer-side tests reference the produced package from that feed.
public static class ThePackageBuilder
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static readonly Dictionary<string, string> feedsByFixture = new(StringComparer.Ordinal);

    public static Task<string> EnsureBuilt() => EnsureBuilt("ThePackage");

    public static async Task<string> EnsureBuilt(string fixtureName)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (feedsByFixture.TryGetValue(fixtureName, out var existing))
            {
                return existing;
            }

            var feed = Path.Combine(Path.GetTempPath(), "sponsorcheck-it-feed", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(feed);

            // Copy the just-built SponsorCheck nupkg from src/../nugets into the feed.
            var srcNugets = TestEnvironment.SrcNugetsDir;
            var sponsorCheckNupkgs = Directory.Exists(srcNugets)
                ? Directory.GetFiles(srcNugets, "SponsorCheck.*.nupkg")
                : [];
            if (sponsorCheckNupkgs.Length != 1)
            {
                throw new InvalidOperationException(
                    sponsorCheckNupkgs.Length == 0
                        ? $"No SponsorCheck nupkg in {srcNugets}. Run `dotnet build src --configuration Release` first."
                        : $"Expected exactly one SponsorCheck.*.nupkg in {srcNugets}, found {sponsorCheckNupkgs.Length}. Clean stale nupkgs and rebuild src.");
            }

            var sponsorCheckNupkg = sponsorCheckNupkgs[0];
            File.Copy(sponsorCheckNupkg, Path.Combine(feed, Path.GetFileName(sponsorCheckNupkg)), overwrite: true);

            // Use the nupkg filename as the source of truth for the version.
            // Fixture csprojs declare Version="$(SponsorCheckVersion)" so they pick up exactly that build.
            var sponsorCheckVersion = ExtractVersion(sponsorCheckNupkg);

            var workDir = TestEnvironment.MakeWorkDir($"{fixtureName}-pack");
            TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDir, "_Shared", fixtureName), workDir);
            TestEnvironment.WriteNugetConfig(workDir, feed);

            // Pack with override list so we don't hit live platforms.
            // Use an isolated packages dir so we don't pull a stale SponsorCheck from the global cache.
            var packagesDir = Path.Combine(feed, ".pkgs");
            Directory.CreateDirectory(packagesDir);
            var result = await DotnetCliRunner.Run(
                "pack",
                Path.Combine(workDir, $"{fixtureName}.csproj"),
                "Release",
                new Dictionary<string, string>
                {
                    ["SponsorListOverride"] = TestEnvironment.OverrideListPath,
                    ["PackageOutputPath"] = feed,
                    ["SponsorCheckVersion"] = sponsorCheckVersion,
                    // Backdate pack so Consumer.RecentSponsor can have a SponsorshipStart that is
                    // both AFTER the pack date and BEFORE today (i.e. not in the future).
                    ["SponsorCheck_PackDateOverride"] = "2024-01-01"
                },
                workDir,
                packagesDir).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to pack {fixtureName}:\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
            }

            feedsByFixture[fixtureName] = feed;
            return feed;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// Like EnsureBuilt, but returns the CLI result without throwing on failure. Use this in
    /// tests that expect the pack to fail (e.g. SC104 on bad metadata) and want to inspect the
    /// build output. Always uses a fresh feed dir — never cached.
    /// When <paramref name="useOverrideList"/> is false the bundler hits the real platform fetch
    /// path instead of the JSON override — used by SC102 (missing credential) which only fires
    /// when a real platform is consulted. The caller must supply <paramref name="extraProperties"/>
    /// that block any ambient token from leaking in (e.g. <c>GitHubToken=""</c>).
    public static async Task<CliResult> TryPack(
        string fixtureName,
        bool useOverrideList = true,
        IReadOnlyDictionary<string, string>? extraProperties = null)
    {
        var feed = Path.Combine(Path.GetTempPath(), "sponsorcheck-it-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feed);

        var srcNugets = TestEnvironment.SrcNugetsDir;
        var sponsorCheckNupkg = Directory.GetFiles(srcNugets, "SponsorCheck.*.nupkg").Single();
        File.Copy(sponsorCheckNupkg, Path.Combine(feed, Path.GetFileName(sponsorCheckNupkg)), overwrite: true);
        var sponsorCheckVersion = ExtractVersion(sponsorCheckNupkg);

        var workDir = TestEnvironment.MakeWorkDir($"{fixtureName}-pack");
        TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDir, "_Shared", fixtureName), workDir);
        TestEnvironment.WriteNugetConfig(workDir, feed);

        var packagesDir = Path.Combine(feed, ".pkgs");
        Directory.CreateDirectory(packagesDir);
        var properties = new Dictionary<string, string>
        {
            ["PackageOutputPath"] = feed,
            ["SponsorCheckVersion"] = sponsorCheckVersion,
            ["SponsorCheck_PackDateOverride"] = "2024-01-01"
        };
        if (useOverrideList)
        {
            properties["SponsorListOverride"] = TestEnvironment.OverrideListPath;
        }

        if (extraProperties != null)
        {
            foreach (var pair in extraProperties)
            {
                properties[pair.Key] = pair.Value;
            }
        }

        return await DotnetCliRunner.Run(
            "pack",
            Path.Combine(workDir, $"{fixtureName}.csproj"),
            "Release",
            properties,
            workDir,
            packagesDir).ConfigureAwait(false);
    }

    static string ExtractVersion(string nupkgPath)
    {
        // SponsorCheck.0.1.1.nupkg -> 0.1.1; SponsorCheck.0.1.1-beta.1.nupkg -> 0.1.1-beta.1
        var name = Path.GetFileNameWithoutExtension(nupkgPath);
        const string prefix = "SponsorCheck.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected nupkg name: {name}");
        }

        return name.Substring(prefix.Length);
    }
}
