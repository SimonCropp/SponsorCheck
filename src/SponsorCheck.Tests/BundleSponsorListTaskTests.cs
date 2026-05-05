public class BundleSponsorListTaskTests
{
    static string BuildTemplate(TempDirectory dir)
    {
        var path = Path.Combine(dir, "ConsumerVerifier.targets");
        File.WriteAllText(path, "<Project><!-- stub --></Project>");
        return path;
    }

    static string WriteOverride(TempDirectory dir, string content)
    {
        var path = Path.Combine(dir, "override.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task SucceedsWithOverrideListSingleAccount()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, """[{"platform":"GitHubSponsors","account":"alice"}]""");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsTrue();
        await Assert.That(File.Exists(task.OutputHashListPath)).IsTrue();
        await Assert.That(File.Exists(task.OutputVerifierTargetsPath)).IsTrue();
        var lines = await File.ReadAllLinesAsync(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo(SponsorHasher.Hash("GitHubSponsors", "alice"));
    }

    [Test]
    public async Task WritesAuthorAccountsFileWithEnabledPlatforms()
    {
        // The bundled AuthorAccounts file is what lets the consumer-side verifier construct
        // platform-specific sponsor URLs in SC001/SC003 messages.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            OpenCollectiveAccountFromRef = "acme-org",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        await Assert.That(task.Execute()).IsTrue();
        var entries = AuthorAccountsFile.Read(task.OutputAuthorAccountsPath);
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.Any(p => p.Key == "GitHubSponsors" && p.Value == "acmecorp")).IsTrue();
        await Assert.That(entries.Any(p => p.Key == "OpenCollective" && p.Value == "acme-org")).IsTrue();
    }

    [Test]
    public async Task PolarMissingTokenSurfacesAsSC102()
    {
        // Polar requires a token. Missing token throws MissingCredentialException,
        // which the task catches and surfaces as SC102 (distinct from the generic SC100).
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            PolarAccountFromRef = "acme",
            // No PolarToken set, no UserSecretsId — token resolution returns null.
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC102");
        await Assert.That(engine.Errors[0].Message).Contains("Polar");
        // Confirm the misleading "(SC102)" suffix in the message was removed when we made the
        // diagnostic structured rather than text-tagged.
        await Assert.That(engine.Errors[0].Message).DoesNotContain("(SC102)");
    }

    [Test]
    public async Task FailsWhenNoPlatformAccount()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC101");
    }

    [Test]
    public async Task BundlesAcrossMultiplePlatforms()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(
            dir,
            """
            [
              {"platform":"GitHubSponsors","account":"alice"},
              {"platform":"GitHubSponsors","account":"bob"},
              {"platform":"OpenCollective","account":"acme-org"},
              {"platform":"Polar","account":"acme"}
            ]
            """);
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            OpenCollectiveAccountFromRef = "acme-org",
            PolarAccountFromVer = "acme",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsTrue();
        var lines = await File.ReadAllLinesAsync(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(4);
        // Sorted ordinal
        for (var i = 1; i < lines.Length; i++)
        {
            await Assert.That(string.CompareOrdinal(lines[i - 1], lines[i])).IsLessThan(0);
        }
    }

    static IReadOnlyDictionary<string, string> Secrets(params (string key, string value)[] entries)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in entries)
        {
            dict[k] = v;
        }

        return dict;
    }

    [Test]
    public async Task ResolveToken_PrefersExplicitOverSecrets()
    {
        var token = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            "ghp_explicit",
            Secrets(("SponsorCheck:GitHubToken", "ghp_from_secrets")));
        await Assert.That(token).IsEqualTo("ghp_explicit");
    }

    [Test]
    public async Task ResolveToken_FallsBackToSecretsForGitHub()
    {
        var token = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            null,
            Secrets(("SponsorCheck:GitHubToken", "ghp_from_secrets")));
        await Assert.That(token).IsEqualTo("ghp_from_secrets");
    }

    [Test]
    public async Task ResolveToken_GitHubKeyDoesNotIncludeSponsors()
    {
        // The GitHubSponsors platform reads "SponsorCheck:GitHubToken" (matching GITHUB_TOKEN env var convention),
        // not "SponsorCheck:GitHubSponsorsToken".
        var miss = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            null,
            Secrets(("SponsorCheck:GitHubSponsorsToken", "ghp_wrong_key")));
        await Assert.That(miss).IsNull();
    }

    [Test]
    public async Task ResolveToken_NonGitHubUsesPlatformIdKey()
    {
        var oc = BundleSponsorListTask.ResolveToken(
            "OpenCollective",
            null,
            Secrets(("SponsorCheck:OpenCollectiveToken", "oc_secret")));
        await Assert.That(oc).IsEqualTo("oc_secret");

        var polar = BundleSponsorListTask.ResolveToken(
            "Polar",
            null,
            Secrets(("SponsorCheck:PolarToken", "polar_secret")));
        await Assert.That(polar).IsEqualTo("polar_secret");
    }

    [Test]
    public async Task ResolveToken_TreatsWhitespaceExplicitTokenAsUnset()
    {
        var token = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            "   ",
            Secrets(("SponsorCheck:GitHubToken", "ghp_from_secrets")));
        await Assert.That(token).IsEqualTo("ghp_from_secrets");
    }

    [Test]
    public async Task ResolveToken_TreatsWhitespaceSecretAsUnset()
    {
        var token = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            null,
            Secrets(("SponsorCheck:GitHubToken", "   ")));
        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task ResolveToken_NoMatchReturnsNull()
    {
        var token = BundleSponsorListTask.ResolveToken(
            "GitHubSponsors",
            null,
            Secrets());
        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task TemplateSubstitution_ReplacesBothPlaceholderForms()
    {
        // The bundler substitutes __SC_PACKAGE_ID__ (sanitized: dots/dashes -> underscores, used in
        // MSBuild target/item names) and __SC_PACKAGE_ID_RAW__ (literal package id, used inside
        // element values). Catch regressions where someone moves a placeholder to attribute
        // position and breaks the >...< substitution shape.
        using var dir = new TempDirectory();
        var templatePath = Path.Combine(dir, "ConsumerVerifier.targets");
        await File.WriteAllTextAsync(
            templatePath,
            """
            <Project>
              <Target Name="_SponsorCheck_Verify___SC_PACKAGE_ID__" />
              <PropertyGroup>
                <_SponsorCheck_ThePackageId>__SC_PACKAGE_ID_RAW__</_SponsorCheck_ThePackageId>
              </PropertyGroup>
            </Project>
            """);

        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = templatePath,
            // ID with dot, dash, and digit — exercise sanitization.
            ThePackageId = "Acme.Lib-2",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "Acme.Lib-2.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        await Assert.That(rendered).Contains("Name=\"_SponsorCheck_Verify_Acme_Lib_2\"");
        await Assert.That(rendered).Contains(">Acme.Lib-2<");
        await Assert.That(rendered).DoesNotContain("__SC_PACKAGE_ID__");
        await Assert.That(rendered).DoesNotContain("__SC_PACKAGE_ID_RAW__");
    }

    [Test]
    public async Task DeterministicOutput()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(
            dir,
            """
            [
              {"platform":"GitHubSponsors","account":"bob"},
              {"platform":"GitHubSponsors","account":"alice"},
              {"platform":"GitHubSponsors","account":"alice"}
            ]
            """);
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt")
        };

        await Assert.That(task.Execute()).IsTrue();
        var lines = await File.ReadAllLinesAsync(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(2); // dedup
    }
}
