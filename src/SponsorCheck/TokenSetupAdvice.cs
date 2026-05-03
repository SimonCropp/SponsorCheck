// Builds the "API token required" message for missing-credential errors. Branches on
// BuildServerDetector.Detected so on CI we recommend env vars only (no per-developer profile to
// hold a user-secrets file), and locally we lead with user-secrets (the recommended convention)
// with the MSBuild property and env var as alternates. Pure overload exists for unit testing
// without flipping real CI env vars.
public static class TokenSetupAdvice
{
    public static string MissingTokenMessage(string platformLabel, string tokenName, string secretKey, string? extra = null) =>
        MissingTokenMessage(platformLabel, tokenName, secretKey, BuildServerDetector.Detected, extra);

    public static string MissingTokenMessage(string platformLabel, string tokenName, string secretKey, bool onBuildServer, string? extra = null)
    {
        var advice = onBuildServer
            ? $"Set the '{tokenName}' env var (CI providers should expose their encrypted secret under this name; MSBuild auto-imports it as the <{tokenName}> property)."
            : $"Run `dotnet user-secrets set {secretKey} <pat>` (recommended for local dev), or set the <{tokenName}> MSBuild property, or set the '{tokenName}' env var.";
        var message = $"{platformLabel}: API token required. {advice}";
        return extra == null ? message : $"{message} {extra}";
    }
}
