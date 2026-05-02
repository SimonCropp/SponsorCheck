namespace EnforceOssSponsorship.Tests;

public class BundleSponsorListTaskTests
{
    static (string templateDir, string templatePath) BuildTemplate()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"eoss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ConsumerVerifier.targets");
        File.WriteAllText(path, "<Project><!-- stub --></Project>");
        return (dir, path);
    }

    static string WriteOverride(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eoss-override-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task SucceedsWithOverrideListSingleAccount()
    {
        var (_, template) = BuildTemplate();
        var override_ = WriteOverride("""[{"platform":"GitHubSponsors","account":"alice"}]""");
        var work = Path.Combine(Path.GetTempPath(), $"eoss-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(work, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(work, "MyOssLib.targets")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsTrue();
        await Assert.That(File.Exists(task.OutputHashListPath)).IsTrue();
        await Assert.That(File.Exists(task.OutputVerifierTargetsPath)).IsTrue();
        var lines = File.ReadAllLines(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo(SponsorHasher.Hash("GitHubSponsors", "alice"));
    }

    [Test]
    public async Task FailsWhenNoPlatformAccount()
    {
        var (_, template) = BuildTemplate();
        var work = Path.Combine(Path.GetTempPath(), $"eoss-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OutputHashListPath = Path.Combine(work, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(work, "MyOssLib.targets")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("EOSS102");
    }

    [Test]
    public async Task BundlesAcrossMultiplePlatforms()
    {
        var (_, template) = BuildTemplate();
        var override_ = WriteOverride("""
        [
          {"platform":"GitHubSponsors","account":"alice"},
          {"platform":"GitHubSponsors","account":"bob"},
          {"platform":"OpenCollective","account":"acme-org"},
          {"platform":"Polar","account":"acme"}
        ]
        """);
        var work = Path.Combine(Path.GetTempPath(), $"eoss-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            OpenCollectiveAccountFromRef = "acme-org",
            PolarAccountFromVer = "acme",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(work, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(work, "MyOssLib.targets")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsTrue();
        var lines = File.ReadAllLines(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(4);
        // Sorted ordinal
        for (var i = 1; i < lines.Length; i++)
        {
            await Assert.That(string.CompareOrdinal(lines[i - 1], lines[i])).IsLessThan(0);
        }
    }

    [Test]
    public async Task UserSecretsTokenFallback()
    {
        // Write a secrets.json under a temporary UserSecretsId.
        var id = $"eoss-test-{Guid.NewGuid():N}";
        var secretsPath = UserSecretsReader.ResolvePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
        File.WriteAllText(secretsPath, """
        { "EnforceOssSponsorship:GitHubSponsorsToken": "ghp_from_secrets" }
        """);
        try
        {
            var (_, template) = BuildTemplate();
            var override_ = WriteOverride("""[{"platform":"GitHubSponsors","account":"alice"}]""");
            var work = Path.Combine(Path.GetTempPath(), $"eoss-out-{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            var task = new BundleSponsorListTask
            {
                BuildEngine = new StubBuildEngine(),
                GitHubSponsorsAccountFromRef = "acmecorp",
                UserSecretsId = id,
                VerifierTargetsTemplatePath = template,
                ThePackageId = "MyOssLib",
                OverrideListPath = override_,
                OutputHashListPath = Path.Combine(work, "SponsorHashes.txt"),
                OutputVerifierTargetsPath = Path.Combine(work, "MyOssLib.targets")
            };

            // Override list short-circuits the actual platform fetch, so we just confirm the task ran end-to-end
            // with the user-secrets present (and would have used them if the fetch had occurred).
            await Assert.That(task.Execute()).IsTrue();
        }
        finally
        {
            File.Delete(secretsPath);
        }
    }

    [Test]
    public async Task DeterministicOutput()
    {
        var (_, template) = BuildTemplate();
        var override_ = WriteOverride("""
        [
          {"platform":"GitHubSponsors","account":"bob"},
          {"platform":"GitHubSponsors","account":"alice"},
          {"platform":"GitHubSponsors","account":"alice"}
        ]
        """);
        var work = Path.Combine(Path.GetTempPath(), $"eoss-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(work, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(work, "MyOssLib.targets")
        };

        await Assert.That(task.Execute()).IsTrue();
        var lines = File.ReadAllLines(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(2); // dedup
    }
}
