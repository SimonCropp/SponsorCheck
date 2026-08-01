using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.InitializePlugins();
        VerifierSettings.ScrubLinesWithReplace(_ => Regex.Replace(
            _,
            "blazor:elementreference=\"[^\"]*\"",
            "blazor:elementreference=\"scrubbed\"",
            RegexOptions.IgnoreCase));
    }
}
