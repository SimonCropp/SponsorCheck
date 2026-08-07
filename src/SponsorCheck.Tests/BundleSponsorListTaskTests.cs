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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
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
        // platform-specific sponsor URLs in SC001/SC005 messages.
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var entries = AuthorAccountsFile.Read(task.OutputAuthorAccountsPath);
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.Any(_ => _ is {Key: "GitHubSponsors", Value: "acmecorp"})).IsTrue();
        await Assert.That(entries.Any(_ => _ is {Key: "OpenCollective", Value: "acme-org"})).IsTrue();
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        var ok = task.Execute();

        await Assert.That(ok).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC101");
    }

    [Test]
    public async Task LandingUrlOverride_WrittenToSidecar()
    {
        // SponsorLandingUrl on the author's SponsorCheck reference is written verbatim into the
        // bundled LandingUrl.txt — the verifier reads this to replace per-platform URLs in
        // SC0xx messages.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            SponsorLandingUrlFromRef = "https://acme.example.com/sponsor",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var written = await File.ReadAllTextAsync(task.OutputLandingUrlPath);
        await Assert.That(written).IsEqualTo("https://acme.example.com/sponsor");
    }

    [Test]
    public async Task LandingUrlOverride_AbsentWhenUnset_WritesEmptySidecar()
    {
        // Bundler always writes the sidecar (for deterministic packaging) but its contents are
        // empty when SponsorLandingUrl is not declared.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(task.OutputLandingUrlPath)).IsTrue();
        var written = await File.ReadAllTextAsync(task.OutputLandingUrlPath);
        await Assert.That(written).IsEqualTo("");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
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

    [Test]
    public async Task OverrideList_NormalizesPlatformIdCasing()
    {
        // Override entries can carry arbitrary platform-id casing. SponsorHasher does NOT case-fold
        // the platform id, and the verifier always hashes with the canonical literal ("GitHubSponsors"),
        // so the bundler must canonicalize the id — otherwise the bundled hash could never match a
        // consumer and every build would fail SC007 despite the override packing cleanly.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, """[{"platform":"githubsponsors","account":"alice"}]""");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var lines = await File.ReadAllLinesAsync(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(1);
        // Hash matches the canonical-cased platform id the verifier computes, not the raw "githubsponsors".
        await Assert.That(lines[0]).IsEqualTo(SponsorHasher.Hash("GitHubSponsors", "alice"));
    }

    [Test]
    public async Task OverrideList_UnknownPlatform_FailsWithSC100()
    {
        // A platform id the registry doesn't recognize would otherwise bundle a hash no verifier can
        // ever match (the verifier only hashes the three known platform literals). Fail at pack time
        // rather than silently shipping dead hashes.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var override_ = WriteOverride(dir, """[{"platform":"GitHub","account":"alice"}]""");
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC100");
        await Assert.That(engine.Errors[0].Message).Contains("Unknown sponsorship platform 'GitHub'");
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
    public async Task ResolveTokens_ReturnsBothWhenBothPresent()
    {
        // Multi-candidate fallback: the bundler tries each token in turn so a stale env-var-promoted
        // explicit token doesn't shadow a working user-secret token (the case that prompted this:
        // an env var GitHubToken left over from before read:user was added, with the user-secret
        // already refreshed to include the new scope).
        var tokens = BundleSponsorListTask.ResolveTokens(
            "GitHubSponsors",
            "ghp_explicit",
            Secrets(("SponsorCheck:GitHubToken", "ghp_from_secrets")));
        await Assert.That(tokens.Count).IsEqualTo(2);
        await Assert.That(tokens[0].Value).IsEqualTo("ghp_explicit");
        await Assert.That(tokens[1].Value).IsEqualTo("ghp_from_secrets");
        // Each candidate carries where it came from, so SC107 can name the stored value to replace.
        await Assert.That(tokens[0].Source).Contains("<GitHubToken> MSBuild property");
        await Assert.That(tokens[1].Source).Contains("SponsorCheck:GitHubToken");
    }

    [Test]
    public async Task ResolveTokens_DedupesIdenticalValues()
    {
        // env var imported as the MSBuild property AND copied into the secret store: try once, not twice.
        var tokens = BundleSponsorListTask.ResolveTokens(
            "GitHubSponsors",
            "ghp_same",
            Secrets(("SponsorCheck:GitHubToken", "ghp_same")));
        await Assert.That(tokens.Count).IsEqualTo(1);
        await Assert.That(tokens[0].Value).IsEqualTo("ghp_same");
    }

    [Test]
    public async Task ResolveTokens_EmptyWhenNothingSet()
    {
        var tokens = BundleSponsorListTask.ResolveTokens("GitHubSponsors", null, Secrets());
        await Assert.That(tokens.Count).IsEqualTo(0);
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        var expectedSanitized = BundleSponsorListTask.Sanitize("Acme.Lib-2");
        await Assert.That(rendered).Contains($"Name=\"_SponsorCheck_Verify_{expectedSanitized}\"");
        await Assert.That(rendered).Contains(">Acme.Lib-2<");
        await Assert.That(rendered).DoesNotContain("__SC_PACKAGE_ID__");
        await Assert.That(rendered).DoesNotContain("__SC_PACKAGE_ID_RAW__");
    }

    [Test]
    public async Task TemplateSubstitution_BindsVersionScopedTaskName()
    {
        // MSBuild's task registry is keyed by task name, and the first UsingTask to claim a name serves
        // the whole project — so two packages bundling different SponsorCheck versions under the bare
        // name VerifySponsorshipTask shared one task instance, and the build failed MSB4064 once their
        // parameter sets diverged. The verifier therefore binds a per-release type name, which the
        // build generates (_SponsorCheck_GenerateVersionedTaskName in SponsorCheck.csproj). Assert the
        // generated type is really there and really is the task — a dropped codegen target would
        // otherwise ship targets naming a type that doesn't exist, failing at consumer build time.
        var scoped = typeof(VerifySponsorshipTask).Assembly.GetType(VersionedTaskName.Verify);
        await Assert.That(scoped).IsNotNull();
        await Assert.That(scoped!.BaseType).IsEqualTo(typeof(VerifySponsorshipTask));
        await Assert.That(VersionedTaskName.Verify).Matches(@"^VerifySponsorshipTask_\d+_\d+_\d+");

        using var dir = new TempDirectory();
        var templatePath = Path.Combine(dir, "ConsumerVerifier.targets");
        await File.WriteAllTextAsync(
            templatePath,
            """
            <Project>
              <UsingTask TaskName="__SC_TASK_NAME__" AssemblyFile="x" />
              <Target Name="_SponsorCheck_Verify___SC_PACKAGE_ID__">
                <__SC_TASK_NAME__ ThePackageId="__SC_PACKAGE_ID_RAW__" />
              </Target>
            </Project>
            """);

        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = templatePath,
            ThePackageId = "Acme.Lib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "Acme.Lib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        await Assert.That(rendered).Contains($"TaskName=\"{VersionedTaskName.Verify}\"");
        await Assert.That(rendered).Contains($"<{VersionedTaskName.Verify} ");
        await Assert.That(rendered).DoesNotContain("__SC_TASK_NAME__");
    }

    [Test]
    public async Task InnerTargetsImport_Set_EmitsImportOfSidecar()
    {
        // When the author package ships its own <PackageId>.targets, SponsorCheck.targets relocates it
        // to a sidecar and passes the sidecar file name here. The bundler must replace the
        // __SC_INNER_IMPORT__ placeholder with an <Import> of that sidecar (guarded by Exists), so the
        // author's own build logic still runs in consumers alongside the verifier.
        using var dir = new TempDirectory();
        var templatePath = Path.Combine(dir, "ConsumerVerifier.targets");
        await File.WriteAllTextAsync(
            templatePath,
            """
            <Project>
            __SC_INNER_IMPORT__
              <Target Name="_SponsorCheck_Verify___SC_PACKAGE_ID__" />
            </Project>
            """);

        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = templatePath,
            ThePackageId = "MyOssLib",
            InnerTargetsImportFileName = "MyOssLib.SponsorCheckInner.targets",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        await Assert.That(rendered).Contains("<Import Project=\"$(MSBuildThisFileDirectory)MyOssLib.SponsorCheckInner.targets\"");
        await Assert.That(rendered).Contains("Condition=\"Exists('$(MSBuildThisFileDirectory)MyOssLib.SponsorCheckInner.targets')\"");
        await Assert.That(rendered).DoesNotContain("__SC_INNER_IMPORT__");
    }

    [Test]
    public async Task InnerTargetsImport_Unset_EmitsNoImport()
    {
        // The common case: no author-owned <PackageId>.targets, so InnerTargetsImportFileName is empty
        // and the placeholder collapses to nothing — no stray <Import> in the verifier.
        using var dir = new TempDirectory();
        var templatePath = Path.Combine(dir, "ConsumerVerifier.targets");
        await File.WriteAllTextAsync(
            templatePath,
            """
            <Project>
            __SC_INNER_IMPORT__
              <Target Name="_SponsorCheck_Verify___SC_PACKAGE_ID__" />
            </Project>
            """);

        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = templatePath,
            ThePackageId = "MyOssLib",
            // InnerTargetsImportFileName left at its default ("").
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        await Assert.That(rendered).DoesNotContain("<Import");
        await Assert.That(rendered).DoesNotContain("__SC_INNER_IMPORT__");
    }

    [Test]
    public async Task OwnerMode_SelectsOwnerTemplateAndSubstitutesOwnerId()
    {
        // When SponsorOwner is set the bundler must render the OWNER template (reads global
        // properties) rather than the per-package template, and substitute both the package-id and
        // owner-id placeholders. __SC_OWNER_ID__ becomes the sanitized guard-property suffix;
        // __SC_OWNER_ID_RAW__ becomes the literal owner id.
        using var dir = new TempDirectory();
        var perPackageTemplate = Path.Combine(dir, "ConsumerVerifier.targets");
        await File.WriteAllTextAsync(perPackageTemplate, "<Project><!-- per-package template --></Project>");
        var ownerTemplate = Path.Combine(dir, "ConsumerVerifierOwner.targets");
        await File.WriteAllTextAsync(
            ownerTemplate,
            """
            <Project>
              <Target Name="_SponsorCheck_Verify___SC_PACKAGE_ID__"
                      Condition="'$(_SponsorCheck_OwnerVerified___SC_OWNER_ID__)' != 'true'" />
              <PropertyGroup>
                <_SponsorCheck_OwnerId>__SC_OWNER_ID_RAW__</_SponsorCheck_OwnerId>
              </PropertyGroup>
            </Project>
            """);

        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            SponsorOwnerFromRef = "acme",
            VerifierTargetsTemplatePath = perPackageTemplate,
            VerifierOwnerTargetsTemplatePath = ownerTemplate,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var rendered = await File.ReadAllTextAsync(task.OutputVerifierTargetsPath);
        await Assert.That(rendered).DoesNotContain("per-package template");
        await Assert.That(rendered).Contains("_SponsorCheck_OwnerId>acme<");
        await Assert.That(rendered).Contains($"_SponsorCheck_Verify_{BundleSponsorListTask.Sanitize("MyOssLib")}");
        await Assert.That(rendered).Contains($"_SponsorCheck_OwnerVerified_{BundleSponsorListTask.Sanitize("acme")}");
        await Assert.That(rendered).DoesNotContain("__SC_OWNER_ID__");
        await Assert.That(rendered).DoesNotContain("__SC_OWNER_ID_RAW__");
    }

    [Test]
    public async Task Sanitize_DistinguishesIdsThatCollideUnderCharReplacement()
    {
        // Package ids that differ only by separator (. vs - vs _) all map to the same
        // alphanumeric+underscore prefix. Without a stable tie-breaker, a consumer that
        // PackageReferences two such packages would import two .targets files that each
        // declare the same _SponsorCheck_Verify_<sanitized> target and fail to load.
        var dotted = BundleSponsorListTask.Sanitize("Acme.Lib");
        var dashed = BundleSponsorListTask.Sanitize("Acme-Lib");
        var underscored = BundleSponsorListTask.Sanitize("Acme_Lib");

        await Assert.That(dotted).IsNotEqualTo(dashed);
        await Assert.That(dotted).IsNotEqualTo(underscored);
        await Assert.That(dashed).IsNotEqualTo(underscored);

        // Stable across calls — the generated targets file name is committed to the nupkg.
        await Assert.That(BundleSponsorListTask.Sanitize("Acme.Lib")).IsEqualTo(dotted);

        // Still MSBuild-identifier-safe: only letters, digits, underscores.
        foreach (var character in dotted)
        {
            await Assert.That(char.IsLetterOrDigit(character) || character == '_').IsTrue();
        }
    }

    [Test]
    public async Task SeverityOverrides_PerCodeMetadata_WritesSidecarFile()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            NoLicenseSpecifiedSeverityOverrideFromRef = "warning",
            LicenseIgnoredSeverityOverrideFromRef = "error",
            InvalidAccountSeverityOverrideFromRef = "message",
            LicenseExpiredSeverityOverrideFromRef = "warning",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var parsed = SeverityOverrideFile.Read(task.OutputSeverityOverridesPath);
        await Assert.That(parsed["SC001"]).IsEqualTo(Severity.Warning);
        await Assert.That(parsed["SC005"]).IsEqualTo(Severity.Error);
        await Assert.That(parsed["SC007"]).IsEqualTo(Severity.Message);
        await Assert.That(parsed["SC009"]).IsEqualTo(Severity.Warning);
    }

    [Test]
    public async Task SeverityOverrides_Empty_WritesEmptySidecar()
    {
        // An empty sidecar still has to be written so the consumer-side path resolution doesn't
        // miss the file. The verifier tolerates missing files but pack-time should be deterministic.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(task.OutputSeverityOverridesPath)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(task.OutputSeverityOverridesPath)).IsEqualTo("");
    }

    [Test]
    public async Task InvalidSponsorOwner_FailsWithSC105()
    {
        // SponsorOwner is baked into consumer-side property names like <acme_GitHubSponsorAccount>,
        // so it must be a clean MSBuild property name prefix. Hyphens, dots, and other punctuation
        // are rejected at pack time rather than producing a broken verifier targets file.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            SponsorOwnerFromRef = "acme-corp",
            VerifierTargetsTemplatePath = template,
            VerifierOwnerTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC105");
        await Assert.That(engine.Errors[0].Message).Contains("acme-corp");
        await Assert.That(engine.Errors[0].Message).Contains("acme-corp_GitHubSponsorAccount");
    }

    [Test]
    [Arguments("acme")]
    [Arguments("Acme")]
    [Arguments("acme_corp")]
    [Arguments("acme1")]
    [Arguments("a")]
    public async Task IsValidOwnerId_Accepts(string ownerId) =>
        await Assert.That(BundleSponsorListTask.IsValidOwnerId(ownerId)).IsTrue();

    [Test]
    [Arguments("")]
    [Arguments("1acme")]        // starts with digit
    [Arguments("_acme")]        // starts with underscore
    [Arguments("acme-corp")]    // hyphen
    [Arguments("acme.corp")]    // dot
    [Arguments("acme corp")]    // space
    [Arguments("acme$")]        // punctuation
    [Arguments("асме")]         // Cyrillic homograph — looks like "acme" but isn't
    [Arguments("café")]         // Latin-1 accented letter
    [Arguments("日本")]          // CJK letters (valid in XML, not allowed here)
    public async Task IsValidOwnerId_Rejects(string ownerId) =>
        await Assert.That(BundleSponsorListTask.IsValidOwnerId(ownerId)).IsFalse();

    [Test]
    public async Task SeverityOverrides_UnknownSeverity_FailsWithSC104()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            NoLicenseSpecifiedSeverityOverrideFromRef = "critical",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC104");
        await Assert.That(engine.Errors[0].Message).Contains("NoLicenseSpecifiedSeverityOverride");
        await Assert.That(engine.Errors[0].Message).Contains("critical");
    }

    [Test]
    public async Task MessageOverrides_PerCodeMetadata_WritesSidecarFile()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            NoLicenseSpecifiedMessageOverrideFromRef = "Please sponsor!",
            LicenseIgnoredMessageOverrideFromRef = "You agreed not to free-ride.",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var parsed = MessageOverrideFile.Read(task.OutputMessageOverridesPath);
        await Assert.That(parsed["SC001"]).IsEqualTo("Please sponsor!");
        await Assert.That(parsed["SC005"]).IsEqualTo("You agreed not to free-ride.");
    }

    [Test]
    public async Task MessageOverrides_Empty_WritesEmptyJsonObject()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(task.OutputMessageOverridesPath)).IsTrue();
        var content = (await File.ReadAllTextAsync(task.OutputMessageOverridesPath)).Trim();
        await Assert.That(content).IsEqualTo("{}");
    }

    [Test]
    public async Task SeverityOverrides_PackageVersionMetadata_AlsoSupported()
    {
        // CPM authors put metadata on PackageVersion. Per-code overrides must flow through both
        // ItemGroup batches the same way the platform-account metadata does.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromVer = "acmecorp",
            LicenseIgnoredSeverityOverrideFromVer = "error",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var parsed = SeverityOverrideFile.Read(task.OutputSeverityOverridesPath);
        await Assert.That(parsed["SC005"]).IsEqualTo(Severity.Error);
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var lines = await File.ReadAllLinesAsync(task.OutputHashListPath);
        await Assert.That(lines.Length).IsEqualTo(2); // dedup
    }

    static ITaskItem MakeExemption(string name, string message, string maxTermMonths = "")
    {
        var item = new Microsoft.Build.Utilities.TaskItem(name);
        item.SetMetadata("Message", message);
        if (maxTermMonths.Length > 0)
        {
            item.SetMetadata("MaxTermMonths", maxTermMonths);
        }

        return item;
    }

    [Test]
    public async Task Exemptions_TwoDefined_WrittenToSidecarAsJson()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = override_,
            SponsorExemptions =
            [
                MakeExemption("Consulting", "Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work."),
                MakeExemption("SmallRevenue", "Consumers under US$10,000 annual gross revenue are exempt.")
            ],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var parsed = SponsorshipExemptionsFile.Read(task.OutputExemptionsPath);
        await Assert.That(parsed["Consulting"].Message).Contains("consulting work");
        await Assert.That(parsed["SmallRevenue"].Message).Contains("US$10,000");
    }

    [Test]
    public async Task Exemptions_None_WritesEmptyJsonObject()
    {
        // Sidecar is always written (deterministic packaging) — empty when no exemptions defined.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var override_ = WriteOverride(dir, "[]");
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
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(task.OutputExemptionsPath)).IsTrue();
        var text = await File.ReadAllTextAsync(task.OutputExemptionsPath);
        await Assert.That(text.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task Exemptions_EmptyName_FailsWithSC106()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = WriteOverride(dir, "[]"),
            SponsorExemptions = [MakeExemption("", "some text")],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors).HasSingleItem();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC106");
        await Assert.That(engine.Errors[0].Message).Contains("empty Name");
    }

    [Test]
    public async Task Exemptions_EmptyMessage_FailsWithSC106()
    {
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = WriteOverride(dir, "[]"),
            SponsorExemptions = [MakeExemption("Consulting", "   ")],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC106");
        await Assert.That(engine.Errors[0].Message).Contains("Consulting");
        await Assert.That(engine.Errors[0].Message).Contains("Message metadata is empty");
    }

    [Test]
    public async Task Exemptions_DuplicateName_FailsWithSC106()
    {
        // Case-insensitive duplicate detection: "Consulting" + "consulting" collide.
        using var dir = new TempDirectory();
        var template = BuildTemplate(dir);
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = template,
            ThePackageId = "MyOssLib",
            OverrideListPath = WriteOverride(dir, "[]"),
            SponsorExemptions =
            [
                MakeExemption("Consulting", "first"),
                MakeExemption("consulting", "second")
            ],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC106");
        await Assert.That(engine.Errors[0].Message).Contains("duplicate definition");
    }

    [Test]
    public async Task Exemptions_MaxTermMonths_WrittenToSidecar()
    {
        using var dir = new TempDirectory();
        var task = new BundleSponsorListTask
        {
            BuildEngine = new StubBuildEngine(),
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = BuildTemplate(dir),
            ThePackageId = "MyOssLib",
            OverrideListPath = WriteOverride(dir, "[]"),
            SponsorExemptions =
            [
                MakeExemption("Consulting", "Consulting carve-out.", "6"),
                MakeExemption("SmallRevenue", "Consumers under US$10,000 annual gross revenue are exempt.")
            ],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsTrue();
        var parsed = SponsorshipExemptionsFile.Read(task.OutputExemptionsPath);
        await Assert.That(parsed["Consulting"].MaxTermMonths).IsEqualTo(6);
        // Unset stays unset — the cap is opt-in per exemption, not per package.
        await Assert.That(parsed["SmallRevenue"].MaxTermMonths).IsNull();
    }

    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("six")]
    [Arguments("6.5")]
    [Arguments("+6")]
    [Arguments("1e2")]
    public async Task Exemptions_InvalidMaxTermMonths_FailsWithSC106(string value)
    {
        // The cap is baked into every consumer build, so a typo has to fail the author's pack
        // rather than silently degrade to an uncapped exemption.
        using var dir = new TempDirectory();
        var engine = new StubBuildEngine();
        var task = new BundleSponsorListTask
        {
            BuildEngine = engine,
            GitHubSponsorsAccountFromRef = "acmecorp",
            VerifierTargetsTemplatePath = BuildTemplate(dir),
            ThePackageId = "MyOssLib",
            OverrideListPath = WriteOverride(dir, "[]"),
            SponsorExemptions = [MakeExemption("Consulting", "Consulting carve-out.", value)],
            OutputHashListPath = Path.Combine(dir, "SponsorHashes.txt"),
            OutputVerifierTargetsPath = Path.Combine(dir, "MyOssLib.targets"),
            OutputPackDatePath = Path.Combine(dir, "PackDate.txt"),
            OutputAuthorAccountsPath = Path.Combine(dir, "AuthorAccounts.txt"),
            OutputSeverityOverridesPath = Path.Combine(dir, "SeverityOverrides.txt"),
            OutputMessageOverridesPath = Path.Combine(dir, "MessageOverrides.json"),
            OutputLandingUrlPath = Path.Combine(dir, "LandingUrl.txt"),
            OutputExemptionsPath = Path.Combine(dir, "Exemptions.json")
        };

        await Assert.That(task.Execute()).IsFalse();
        await Assert.That(engine.Errors[0].Code).IsEqualTo("SC106");
        await Assert.That(engine.Errors[0].Message).Contains("Consulting");
        await Assert.That(engine.Errors[0].Message).Contains("MaxTermMonths");
    }
}
