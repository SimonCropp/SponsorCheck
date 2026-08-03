namespace SponsorCheck.Web;

/// <summary>
/// Canonical urls into the repo docs. Shared by the wizard ui and the config generators so the two
/// can't drift.
/// </summary>
public static class DocLinks
{
    public const string Base = "https://github.com/SimonCropp/SponsorCheck";
    public const string AuthorSetup = Base + "/blob/main/docs/AuthorSetup.md";
    public const string ConsumerUsage = Base + "/blob/main/docs/ConsumerUsage.md";
    public const string BundlerCodes = Base + "/blob/main/docs/BundlerDiagnosticCodes.md";
    public const string VerifierCodes = Base + "/blob/main/docs/VerifierDiagnosticCodes.md";

    public const string SeverityOverrides = AuthorSetup + "#tuning-verifier-severity-and-message-text";
    public const string Exemptions = AuthorSetup + "#defining-exemptions";
    public const string LandingUrl = AuthorSetup + "#custom-sponsor-landing-url";

    /// <summary>
    /// Deep link to a single <c>SC0xx</c> section in the verifier code reference.
    /// </summary>
    public static string VerifierCode(string code) => $"{VerifierCodes}#{code.ToLowerInvariant()}";
}
