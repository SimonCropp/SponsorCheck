static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyPlaywright.Initialize();
        VerifyDiffPlex.Initialize();
        VerifierSettings.InitializePlugins();

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
}
