namespace SponsorCheck.IntegrationTests;

/// One-time setup that packs the canonical _Shared/ThePackage fixture into a per-suite local feed dir.
/// Subsequent consumer-side tests reference ThePackage from that feed.
public static class ThePackageBuilder
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static string? localFeedDir;

    public static async Task<string> EnsureBuilt()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (localFeedDir != null)
            {
                return localFeedDir;
            }

            var feed = Path.Combine(Path.GetTempPath(), "sponsorcheck-it-feed", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(feed);

            // Copy the latest SponsorCheck nupkg from src/../nugets into the feed.
            var srcNugets = TestEnvironment.SrcNugetsDir;
            if (!Directory.Exists(srcNugets) || Directory.GetFiles(srcNugets, "SponsorCheck.*.nupkg").Length == 0)
            {
                throw new InvalidOperationException(
                    $"No SponsorCheck nupkg in {srcNugets}. Run `dotnet build src --configuration Release` first.");
            }

            foreach (var nupkg in Directory.GetFiles(srcNugets, "SponsorCheck.*.nupkg"))
            {
                File.Copy(nupkg, Path.Combine(feed, Path.GetFileName(nupkg)), overwrite: true);
            }

            // Copy ThePackage fixture into a working dir.
            var workDir = TestEnvironment.MakeWorkDir("ThePackage-pack");
            TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDir, "_Shared", "ThePackage"), workDir);
            TestEnvironment.WriteNugetConfig(workDir, feed);

            // Pack with override list so we don't hit live platforms.
            // Use an isolated packages dir so we don't pull a stale SponsorCheck from the global cache.
            var packagesDir = Path.Combine(feed, ".pkgs");
            Directory.CreateDirectory(packagesDir);
            var result = await DotnetCliRunner.Run(
                "pack",
                Path.Combine(workDir, "ThePackage.csproj"),
                "Release",
                new Dictionary<string, string>
                {
                    ["SponsorListOverride"] = TestEnvironment.OverrideListPath,
                    ["PackageOutputPath"] = feed,
                    // Backdate pack so Consumer.RecentSponsor can have a SponsorshipStart that is
                    // both AFTER the pack date and BEFORE today (i.e. not in the future).
                    ["SponsorCheck_PackDateOverride"] = "2024-01-01"
                },
                workDir,
                packagesDir).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to pack ThePackage:\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
            }

            localFeedDir = feed;
            return feed;
        }
        finally
        {
            Gate.Release();
        }
    }
}
