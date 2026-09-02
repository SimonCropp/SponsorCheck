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
        "SponsorshipExemptionUntil",
        "SponsorshipStart",
        "SponsorshipPrivateUntil"
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
            "SponsorExemption",
            "PrivateSponsorMaxTermMonths"
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

        // The owner id element is package-scoped, so the template holds a token rather than the final
        // name. Pin the contract against what it renders to — mirroring the substitution in
        // BundleSponsorListTask — since matching rendered output is what the parser actually does.
        var ownerTemplate = ReadSrc("SponsorCheck", "EmbeddedTemplates", "ConsumerVerifierOwner.targets");
        var rendered = ownerTemplate
            .Replace("__SC_PACKAGE_ID__", "SamplePackage_a1b2c3d4")
            .Replace(">__SC_OWNER_ID_RAW__<", ">acme<");
        var match = Regex.Match(rendered, NupkgParser.OwnerIdElementPattern);
        await Assert.That(match.Success).IsTrue().Because("NupkgParser must find the owner id in a rendered owner verifier");
        await Assert.That(match.Groups[2].Value).IsEqualTo("acme");

        // Packages published before scoping carry the bare element and are still on nuget.org, so the
        // parser has to keep reading them.
        var legacy = Regex.Match("<_SponsorCheck_OwnerId>acme</_SponsorCheck_OwnerId>", NupkgParser.OwnerIdElementPattern);
        await Assert.That(legacy.Success).IsTrue().Because("pre-scoping packages must stay readable");
        await Assert.That(legacy.Groups[2].Value).IsEqualTo("acme");

        // Same contract for the private-sponsorship cap, which also rides in the rendered targets
        // rather than a sidecar file. Both templates carry it, so both must stay parseable.
        foreach (var (name, content) in new[] { ("ConsumerVerifier.targets", template), ("ConsumerVerifierOwner.targets", ownerTemplate) })
        {
            var renderedCap = content
                .Replace("__SC_PACKAGE_ID__", "SamplePackage_a1b2c3d4")
                .Replace(">__SC_PRIVATE_MAX_MONTHS_RAW__<", ">6<");
            var capMatch = Regex.Match(renderedCap, NupkgParser.PrivateSponsorMaxTermMonthsElementPattern);
            await Assert.That(capMatch.Success).IsTrue().Because($"NupkgParser must find the private-sponsorship cap in a rendered {name}");
            await Assert.That(capMatch.Groups[2].Value).IsEqualTo("6");
        }
    }

    /// <summary>
    /// MonthBound restates the verifier's month arithmetic, which the wizard cannot reference. The
    /// wizard uses it to tell a consumer which months a build will accept, so a drift means the
    /// wizard names a ceiling the verifier disagrees with — the exact class of bug those callouts
    /// exist to catch.
    /// </summary>
    [Test]
    public async Task WizardMonthArithmeticMatchesTheVerifiers()
    {
        var verifier = ReadSrc("SponsorCheck", "DecisionApplier.cs");
        var wizard = ReadSrc("SponsorCheck.Web", "Models", "MonthBound.cs");

        // Calendar-field arithmetic rather than DateTime.AddMonths, so a claim at the calendar
        // extreme cannot overflow. Compared verbatim because both copies are one expression.
        foreach (var line in new[]
                 {
                     "var total = utcNow.Year * 12 + (utcNow.Month - 1) + months;",
                     "return (total / 12, total % 12 + 1);"
                 })
        {
            await Assert.That(verifier).Contains(line);
            await Assert.That(wizard).Contains(line);
        }

        // Expiry is month-granular in both: the named month is valid through its own end.
        foreach (var fragment in new[] { "utcNow.Year > year", "utcNow.Year == year && utcNow.Month > month" })
        {
            await Assert.That(verifier).Contains(fragment);
            await Assert.That(wizard).Contains(fragment);
        }
    }

    /// <summary>
    /// The wizard can't reference the task assembly, so it carries its own copy of the default
    /// private-sponsorship term. A drift between the two would have the wizard tell consumers a cap
    /// the verifier doesn't enforce.
    /// </summary>
    [Test]
    public async Task WizardPrivateSponsorDefaultMatchesTaskDefault()
    {
        var source = ReadSrc("SponsorCheck", "PrivateSponsorTerm.cs");
        var match = Regex.Match(source, @"DefaultMaxTermMonths\s*=\s*(\d+)\s*;");
        await Assert.That(match.Success).IsTrue().Because("PrivateSponsorTerm.DefaultMaxTermMonths should be a plain integer literal");
        await Assert.That(PackageFacts.DefaultPrivateSponsorMaxTermMonths.ToString()).IsEqualTo(match.Groups[1].Value);

        // The author metadata name is typed by the wizard's generator and read by the bundler.
        var nameMatch = Regex.Match(source, @"AuthorMetadataName\s*=\s*""([^""]+)""");
        await Assert.That(nameMatch.Success).IsTrue();
        var authorGenerator = ReadSrc("SponsorCheck.Web", "Services", "AuthorConfigGenerator.cs");
        await Assert.That(authorGenerator).Contains($"\"{nameMatch.Groups[1].Value}\"");
    }

    /// <summary>
    /// Every character the wizard renders must be covered by a bundled webfont. A character outside
    /// the shipped subsets falls through to whatever the OS supplies, whose advance width differs
    /// per platform — that re-wraps prose and moves the height of the ScreenSnapshotTests PNGs,
    /// which is exactly the drift the bundled fonts exist to remove. The unicode-range descriptors
    /// in app.css are the source of truth, so adding a glyph to a font means widening them here too.
    /// </summary>
    [Test]
    public async Task ShippedFontsCoverRenderedText()
    {
        var css = ReadSrc("SponsorCheck.Web", "wwwroot", "css", "app.css");
        var covered = ParseUnicodeRanges(css);
        await Assert.That(covered.Count).IsGreaterThan(0).Because("app.css should declare unicode-range descriptors");

        var uncovered = new SortedSet<char>(RenderedCharacters().Where(_ => !covered.Contains(_)));

        await Assert.That(uncovered)
            .IsEmpty()
            .Because(
                "these characters have no bundled glyph and would fall back to a system font: " +
                string.Join(", ", uncovered.Select(_ => $"U+{(int) _:X4} '{_}'")));
    }

    /// <summary>Text the wizard actually renders, taken from the html snapshots plus the razor sources.</summary>
    static IEnumerable<char> RenderedCharacters()
    {
        var testDirectory = RepoPaths.SrcFile("SponsorCheck.Web.Tests");
        foreach (var file in Directory.EnumerateFiles(testDirectory, "*.verified.html"))
        {
            // strip markup: attribute values are urls and css classes, not rendered text
            var text = Regex.Replace(File.ReadAllText(file), "<[^>]*>", " ");
            foreach (var character in WebUtility.HtmlDecode(text))
            {
                yield return character;
            }
        }

        var webDirectory = RepoPaths.SrcFile("SponsorCheck.Web");
        foreach (var razor in Directory.EnumerateFiles(webDirectory, "*.razor", SearchOption.AllDirectories))
        {
            foreach (var character in File.ReadAllText(razor))
            {
                yield return character;
            }
        }
    }

    static HashSet<char> ParseUnicodeRanges(string css)
    {
        var covered = new HashSet<char>
        {
            // markup and source formatting, never glyphs on screen
            '\r',
            '\n',
            '\t',
            '﻿'
        };

        foreach (Match declaration in Regex.Matches(css, @"unicode-range:\s*([^;]+);"))
        {
            foreach (Match range in Regex.Matches(declaration.Groups[1].Value, @"U\+([0-9A-Fa-f]+)(?:-([0-9A-Fa-f]+))?"))
            {
                var start = Convert.ToInt32(range.Groups[1].Value, 16);
                var end = range.Groups[2].Success ? Convert.ToInt32(range.Groups[2].Value, 16) : start;
                for (var code = start; code <= end; code++)
                {
                    covered.Add((char) code);
                }
            }
        }

        return covered;
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
            foreach (Match match in Regex.Matches(await File.ReadAllTextAsync(source), @"\bSC\d{3}\b"))
            {
                codes.Add(match.Value);
            }
        }

        await Assert.That(codes.Count).IsGreaterThan(0);

        var verifierDocs = await File.ReadAllTextAsync(RepoPaths.RepoFile("docs", "VerifierDiagnosticCodes.md"));
        var bundlerDocs = await File.ReadAllTextAsync(RepoPaths.RepoFile("docs", "BundlerDiagnosticCodes.md"));
        foreach (var code in codes.OrderBy(_ => _, StringComparer.Ordinal))
        {
            var docs = code.StartsWith("SC1", StringComparison.Ordinal) ? bundlerDocs : verifierDocs;
            await Assert.That(docs).Contains($"### {code}").Because($"the wizard mentions {code}, so the docs must define it");
        }
    }

    [Test]
    public async Task DocLinkAnchorsResolveToHeadings()
    {
        var links = typeof(DocLinks)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(_ => (string) _.GetRawConstantValue()!)
            .Concat(OverrideInfo.All.SelectMany(_ => _.CodeList).Select(DocLinks.VerifierCode))
            .Where(_ => _.Contains('#'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(_ => _, StringComparer.Ordinal);

        foreach (var link in links)
        {
            var split = link.Split('#');
            var file = Path.GetFileName(split[0]);
            var markdown = await File.ReadAllTextAsync(RepoPaths.RepoFile("docs", file));
            var anchors = Regex.Matches(markdown, "^#+ (.+)$", RegexOptions.Multiline)
                .Select(_ => GitHubAnchor(_.Groups[1].Value));
            await Assert.That(anchors).Contains(split[1]).Because($"{link} must point at a real heading");
        }
    }

    /// <summary>
    /// GitHub's heading slug: lowercase, markdown links flattened to their text, punctuation dropped,
    /// spaces to dashes.
    /// </summary>
    static string GitHubAnchor(string heading)
    {
        var text = Regex.Replace(heading.Trim(), @"\[([^\]]*)\]\([^)]*\)", "$1").ToLowerInvariant();
        text = Regex.Replace(text, @"[^\w\- ]", "");
        return text.Replace(' ', '-');
    }
}
