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

            // Copy the latest SponsorCheck nupkg from src/../nugets into the feed.
            var srcNugets = TestEnvironment.SrcNugetsDir;
            var sponsorCheckNupkgs = Directory.Exists(srcNugets)
                ? Directory.GetFiles(srcNugets, "SponsorCheck.*.nupkg")
                : [];
            if (sponsorCheckNupkgs.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No SponsorCheck nupkg in {srcNugets}. Run `dotnet build src --configuration Release` first.");
            }

            foreach (var nupkg in sponsorCheckNupkgs)
            {
                File.Copy(nupkg, Path.Combine(feed, Path.GetFileName(nupkg)), overwrite: true);
            }

            // Use whichever SponsorCheck.*.nupkg sits in nugets/ as the source of truth for the version.
            // Fixture csprojs declare Version="$(SponsorCheckVersion)" so they pick up exactly that build.
            var sponsorCheckVersion = ExtractVersion(sponsorCheckNupkgs[0]);

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
