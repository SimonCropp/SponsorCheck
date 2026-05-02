namespace SponsorCheck.IntegrationTests;

public static class TestEnvironment
{
    static readonly Lazy<string> RepoRootValue = new(FindRepoRoot);
    public static string RepoRoot => RepoRootValue.Value;

    public static string SrcNugetsDir => Path.Combine(RepoRoot, "nugets");
    public static string FixturesDir => Path.Combine(RepoRoot, "IntegrationTests", "Fixtures");
    public static string OverrideListPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sponsors-override.json");

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "license.txt")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("Could not locate repo root (no license.txt found above test bin).");
        }

        return dir.FullName;
    }

    public static string MakeWorkDir([CallerMemberName] string caller = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "sponsorcheck-it", $"{caller}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    public static void WriteNugetConfig(string projectDir, string localFeedDir)
    {
        var content = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="local" value="{localFeedDir}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
        </configuration>
        """;
        File.WriteAllText(Path.Combine(projectDir, "nuget.config"), content);
    }
}
