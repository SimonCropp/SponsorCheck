// Resolves platform tokens for the live integration-style tests (LiveLookup).
// Env var first (so CI like AppVeyor can inject secrets without writing a user-secrets file),
// then user-secrets (the local-dev convention). Pure overloads exist for unit testing without
// mutating real env vars or the user-secrets directory.
public static class LiveTokenResolver
{
    // Matches <UserSecretsId> in src/SponsorCheck/SponsorCheck.csproj.
    public const string UserSecretsId = "0b81e813-4e7d-40f9-810b-9bd2cddd69e4";

    public static string ResolveOrSkip(string envVarName, string secretKey, string platformLabel, string? localExtra = null)
    {
        var token = TryResolve(envVarName, secretKey);
        if (token != null)
        {
            return token;
        }

        Skip.Test(BuildSkipMessage(envVarName, secretKey, platformLabel, BuildServerDetector.Detected, localExtra));
        return null!; // unreachable: Skip.Test throws
    }

    public static string? TryResolve(string envVarName, string secretKey) =>
        Resolve(
            Environment.GetEnvironmentVariable(envVarName),
            UserSecretsReader.Read(UserSecretsId),
            secretKey);

    public static string? Resolve(string? envValue, IReadOnlyDictionary<string, string> secrets, string secretKey)
    {
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        return secrets.TryGetValue(secretKey, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public static string BuildSkipMessage(string envVarName, string secretKey, string platformLabel, bool onBuildServer, string? localExtra = null)
    {
        // On a build server: lead with env-var advice (no per-developer user-secrets profile).
        // Locally: lead with user-secrets (the recommended convention) and mention env-var as alt.
        if (onBuildServer)
        {
            var msg = $"{platformLabel}: live test skipped. Set env var '{envVarName}' on this build server, or '{secretKey}' in a user-secrets file.";
            return localExtra == null ? msg : $"{msg} {localExtra}";
        }
        else
        {
            var msg = $"{platformLabel}: live test skipped. Run `dotnet user-secrets set {secretKey} <pat>` in src/SponsorCheck (or set env var '{envVarName}').";
            return localExtra == null ? msg : $"{msg} {localExtra}";
        }
    }
}
