static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyPlaywright.Initialize();
        VerifyDiffPlex.Initialize();
        VerifierSettings.InitializePlugins();

        // PNG baselines are authored on Windows; SSIM comparison absorbs the font-rendering
        // differences on the Linux CI images while still catching real layout changes.
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
