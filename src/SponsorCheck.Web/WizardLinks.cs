namespace SponsorCheck.Web;

/// <summary>
/// Canonical urls into the deployed wizard. One place so the generators, the docs and the tests
/// can't drift. <see cref="Package"/> is the entry point a package's own docs link to.
/// </summary>
public static class WizardLinks
{
    public const string Base = "https://simoncropp.github.io/SponsorCheck";
    public const string Consumer = Base + "/consumer";
    public const string Author = Base + "/author";

    /// <summary>
    /// The package-specific flow. Escaping is a no-op for a valid NuGet id (letters, digits, dots,
    /// underscores, dashes); it only defends the author flow, where the id is free text.
    /// </summary>
    public static string Package(string packageId) =>
        $"{Base}/package/{Uri.EscapeDataString(packageId.Trim())}";
}
