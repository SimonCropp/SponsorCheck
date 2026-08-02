using VerifyTests.DiffPlex;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyPlaywright.Initialize();
        VerifyDiffPlex.Initialize(OutputType.Compact);
        VerifierSettings.InitializePlugins();

        // Playwright hands back the page markup as one long line, which makes a snapshot diff
        // unreadable and hides where a change actually landed. AngleSharp re-serializes it as an
        // indented tree, so a diff points at the element that moved. The same pass drops Blazor's
        // <!--!--> render markers, which outnumber the real markup and carry no information.
        HtmlPrettyPrint.All(ScrubBlazorMarkers);

        // The wizard bundles its own fonts (see wwwroot/fonts/readme.md), so layout — and therefore
        // screenshot dimensions — match on every OS. What still differs is rasterization: FreeType
        // and DirectWrite hint and antialias the same outlines differently. SSIM compares structure
        // rather than exact pixels, so a lenient threshold absorbs that while still catching real
        // layout changes.
        VerifierSettings.UseSsimForPng(.7);

        VerifierSettings.ScrubLinesWithReplace(_ => Regex.Replace(
            _,
            "blazor:elementreference=\"[^\"]*\"",
            "blazor:elementreference=\"scrubbed\"",
            RegexOptions.IgnoreCase));

        // The author flow surfaces the current SponsorCheck version (generated from $(Version)) in
        // placeholders; scrub it so html screen snapshots survive version bumps.
        VerifierSettings.AddScrubber("html", _ => _.Replace(WizardDefaults.SponsorCheckVersion, "{SponsorCheckVersion}"));
    }

    const string marker = "<!--!-->";

    /// <summary>
    /// Removes the <c>&lt;!--!--&gt;</c> markers Blazor emits around every rendered region. Inside
    /// &lt;title&gt; the marker is text rather than a comment node, because title content is parsed
    /// as raw text, so both forms are handled.
    /// </summary>
    static void ScrubBlazorMarkers(INodeList nodes)
    {
        // materialized since removing a node mutates the tree being walked
        foreach (var comment in nodes.DescendantsAndSelf<IComment>().ToList())
        {
            if (comment.Data == "!")
            {
                comment.RemoveFromParent();
            }
        }

        foreach (var text in nodes.DescendantsAndSelf<IText>())
        {
            if (text.Data.Contains(marker))
            {
                text.Data = text.Data.Replace(marker, "");
            }
        }
    }
}
