namespace SponsorCheck.Web.Tests;

/// <summary>
/// Anti-rot checks: every name the wizard emits must exist in the real SponsorCheck MSBuild
/// targets/templates/docs shipped from this repo. Drift fails these tests, which run in the Pages
/// deploy workflow — so a stale wizard blocks deployment instead of publishing wrong guidance.
/// </summary>
public class RepoContractTests
{
    static string ReadSrc(params string[] segments) => File.ReadAllText(RepoPaths.SrcFile(segments));

    static readonly string[] consumerModeNames =
    [
        "SponsorshipLicensedUntil",
        "SponsorshipLicenseIgnored",
        "SponsorshipExemption",
        "SponsorshipStart"
    ];

    static IEnumerable<string> ConsumerNames =>
        Platform.All.Select(_ => _.ConsumerAccountMetadata).Concat(consumerModeNames);

    [Test]
    public async Task WizardDefaultVersionMatchesDirectoryBuildProps()
    {
        var props = XDocument.Load(RepoPaths.SrcFile("Directory.Build.props"));
        var version = props.Descendants("Version").Single().Value;
        await Assert.That(WizardDefaults.SponsorCheckVersion).IsEqualTo(version);
    }

    [Test]
    public async Task ConsumerMetadataNamesExistInVerifierTargets()
    {
        var targets = ReadSrc("SponsorCheck", "EmbeddedTemplates", "ConsumerVerifier.targets");
        foreach (var name in ConsumerNames)
        {
            await Assert.That(targets).Contains(name).Because($"consumer metadata '{name}' should exist in ConsumerVerifier.targets");
        }
    }

    [Test]
    public async Task ConsumerPropertyNamesExistInOwnerVerifierTargets()
    {
        var targets = ReadSrc("SponsorCheck", "EmbeddedTemplates", "ConsumerVerifierOwner.targets");
        foreach (var name in ConsumerNames)
        {
            var prefixed = $"__SC_OWNER_PREFIX__{name}";
            await Assert.That(targets).Contains(prefixed).Because($"owner-mode property '{prefixed}' should exist in ConsumerVerifierOwner.targets");
        }
    }

    [Test]
    public async Task AuthorMetadataNamesExistInBundlerTargets()
    {
        var targets = ReadSrc("SponsorCheck", "build", "SponsorCheck.targets");
        var names = new List<string>
        {
            "SponsorOwner",
            "CheckTransitiveReferences",
            "SponsorLandingUrl",
            "SponsorCheckBundleInPullRequest",
            "SponsorExemption"
        };
        names.AddRange(Platform.All.Select(_ => _.AuthorAccountMetadata));
        names.AddRange(Platform.All.Select(_ => _.TokenProperty));
        foreach (var info in OverrideInfo.All)
        {
            names.Add(info.SeverityMetadata);
            names.Add(info.MessageMetadata);
        }

        foreach (var name in names)
        {
            await Assert.That(targets).Contains(name).Because($"author-side name '{name}' should exist in build/SponsorCheck.targets");
        }
    }

    [Test]
    public async Task OverrideInfoMatchesOverrideableCodes()
    {
        var source = ReadSrc("SponsorCheck", "OverrideableCodes.cs");
        var matches = Regex.Matches(source, """new\("(SC\d+)", "(SC\d+)", "(SC\d+)", "(\w+)"\)""");
        var parsed = matches
            .Select(_ => (Codes: $"{_.Groups[1].Value}/{_.Groups[2].Value}/{_.Groups[3].Value}", Stem: _.Groups[4].Value))
            .ToList();

        await Assert.That(parsed.Count).IsEqualTo(OverrideInfo.All.Count);
        foreach (var info in OverrideInfo.All)
        {
            await Assert.That(parsed).Contains((info.Codes, info.Stem));
        }
    }

    [Test]
    public async Task UserSecretKeysFollowConvention()
    {
        foreach (var platform in Platform.All)
        {
            await Assert.That(platform.UserSecretKey).IsEqualTo($"SponsorCheck:{platform.TokenProperty}");
        }

        // Anchors the "SponsorCheck:" prefix convention to the real token resolution code.
        var bundler = ReadSrc("SponsorCheck", "BundleSponsorListTask.cs");
        await Assert.That(bundler).Contains("SponsorCheck:GitHubToken");
    }

    [Test]
    public async Task PlatformWireIdsExistInBundlerSource()
    {
        // The wire ids key the SponsorCheck.AuthorAccounts.txt sidecar that NupkgParser reads.
        var bundler = ReadSrc("SponsorCheck", "BundleSponsorListTask.cs");
        foreach (var platform in Platform.All)
        {
            await Assert.That(bundler).Contains($"\"{platform.WireId}\"").Because($"wire id '{platform.WireId}' should exist in BundleSponsorListTask.cs");
        }
    }

    [Test]
    public async Task SidecarFileNamesMatchVerifierTemplates()
    {
        // NupkgParser reads these names out of published nupkgs; the templates define what gets packed.
        var template = ReadSrc("SponsorCheck", "EmbeddedTemplates", "ConsumerVerifier.targets");
        string[] sidecars =
        [
            NupkgParser.HashesFileName,
            NupkgParser.PackDateFileName,
            NupkgParser.AuthorAccountsFileName,
            NupkgParser.SeverityOverridesFileName,
            NupkgParser.LandingUrlFileName,
            NupkgParser.ExemptionsFileName
        ];
        foreach (var sidecar in sidecars)
        {
            await Assert.That(template).Contains(sidecar).Because($"sidecar '{sidecar}' should exist in ConsumerVerifier.targets");
        }

        var ownerTemplate = ReadSrc("SponsorCheck", "EmbeddedTemplates", "ConsumerVerifierOwner.targets");
        await Assert.That(ownerTemplate).Contains(NupkgParser.OwnerIdElement);
    }

    [Test]
    public async Task MentionedDiagnosticCodesAreDocumented()
    {
        var webDirectory = RepoPaths.SrcFile("SponsorCheck.Web");
        var sources = Directory.EnumerateFiles(webDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(webDirectory, "*.razor", SearchOption.AllDirectories))
            .Where(_ => !_.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !_.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(source), @"\bSC\d{3}\b"))
            {
                codes.Add(match.Value);
            }
        }

        await Assert.That(codes.Count).IsGreaterThan(0);

        var verifierDocs = File.ReadAllText(RepoPaths.RepoFile("docs", "VerifierDiagnosticCodes.md"));
        var bundlerDocs = File.ReadAllText(RepoPaths.RepoFile("docs", "BundlerDiagnosticCodes.md"));
        foreach (var code in codes.OrderBy(_ => _, StringComparer.Ordinal))
        {
            var docs = code.StartsWith("SC1", StringComparison.Ordinal) ? bundlerDocs : verifierDocs;
            await Assert.That(docs).Contains($"### {code}").Because($"the wizard mentions {code}, so the docs must define it");
        }
    }
}
