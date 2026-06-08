// Resolves platform tokens for the live integration-style tests (LiveLookup).
// Env var first (so CI like AppVeyor can inject secrets without writing a user-secrets file),
// then user-secrets (the local-dev convention). Returns the full candidate list so the caller
// can try each in turn — a stale env-var token shouldn't shadow a working user-secret token.
// Pure overloads exist for unit testing without mutating real env vars or the user-secrets directory.
public static class LiveTokenResolver
{
    // Matches <UserSecretsId> in src/SponsorCheck/SponsorCheck.csproj.
    public const string UserSecretsId = "0b81e813-4e7d-40f9-810b-9bd2cddd69e4";

    public static IReadOnlyList<string> ResolveAllOrSkip(string envVarName, string secretKey, string platformLabel, string? extra = null)
    {
        var tokens = TryResolveAll(envVarName, secretKey);
        if (tokens.Count > 0)
        {
            return tokens;
        }

        Skip.Test(BuildSkipMessage(envVarName, secretKey, platformLabel, BuildServerDetector.Detected, extra));
        return []; // unreachable: Skip.Test throws
    }

    public static string ResolveOrSkip(string envVarName, string secretKey, string platformLabel, string? extra = null) =>
        ResolveAllOrSkip(envVarName, secretKey, platformLabel, extra)[0];

    public static IReadOnlyList<string> TryResolveAll(string envVarName, string secretKey) =>
        ResolveAll(
            Environment.GetEnvironmentVariable(envVarName),
            UserSecretsReader.Read(UserSecretsId),
            secretKey);

    public static string? TryResolve(string envVarName, string secretKey)
    {
        var tokens = TryResolveAll(envVarName, secretKey);
        return tokens.Count == 0 ? null : tokens[0];
    }

    public static IReadOnlyList<string> ResolveAll(string? envValue, IReadOnlyDictionary<string, string> secrets, string secretKey)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            tokens.Add(envValue!);
        }

        if (secrets.TryGetValue(secretKey, out var value) &&
            !string.IsNullOrWhiteSpace(value) &&
            !tokens.Contains(value, StringComparer.Ordinal))
        {
            tokens.Add(value);
        }

        return tokens;
    }

    public static string? Resolve(string? envValue, IReadOnlyDictionary<string, string> secrets, string secretKey)
    {
        var tokens = ResolveAll(envValue, secrets, secretKey);
        return tokens.Count == 0 ? null : tokens[0];
    }

    public static string BuildSkipMessage(string envVarName, string secretKey, string platformLabel, bool onBuildServer, string? extra = null)
    {
        // On a build server: env var only (no per-developer user-secrets profile to point at).
        // Locally: lead with user-secrets (the recommended convention) and mention env-var as alt.
        var advice = onBuildServer
            ? $"Set the '{envVarName}' env var on this build server."
            : $"Run `dotnet user-secrets set {secretKey} <pat>` in src/SponsorCheck (or set the '{envVarName}' env var).";
        var message = $"{platformLabel}: live test skipped. {advice}";
        return extra == null ? message : $"{message} {extra}";
    }
}
