public class TokenSetupAdviceTests
{
    [Test]
    public async Task OnBuildServer_RecommendsEnvVarOnly()
    {
        var msg = TokenSetupAdvice.MissingTokenMessage(
            "GitHub Sponsors",
            "GitHubToken",
            "SponsorCheck:GitHubToken",
            onBuildServer: true);
        await Assert.That(msg).Contains("GitHub Sponsors");
        await Assert.That(msg).Contains("API token required");
        await Assert.That(msg).Contains("'GitHubToken' env var");
        // CI has no per-developer profile, so don't suggest user-secrets at all.
        await Assert.That(msg).DoesNotContain("user-secrets");
        await Assert.That(msg).DoesNotContain("dotnet user-secrets");
    }

    [Test]
    public async Task LocalDev_LeadsWithUserSecrets()
    {
        var msg = TokenSetupAdvice.MissingTokenMessage(
            "Polar",
            "PolarToken",
            "SponsorCheck:PolarToken",
            onBuildServer: false);
        await Assert.That(msg).Contains("Polar");
        await Assert.That(msg).Contains("dotnet user-secrets set SponsorCheck:PolarToken");
        await Assert.That(msg).Contains("<PolarToken> MSBuild property");
        await Assert.That(msg).Contains("'PolarToken' env var");
        await Assert.That(msg.IndexOf("user-secrets", StringComparison.Ordinal))
            .IsLessThan(msg.IndexOf("env var", StringComparison.Ordinal));
    }

    [Test]
    public async Task AppendsExtra()
    {
        var msg = TokenSetupAdvice.MissingTokenMessage(
            "GitHub Sponsors",
            "GitHubToken",
            "SponsorCheck:GitHubToken",
            onBuildServer: true,
            extra: "Unauthenticated GitHub API calls hit a low rate limit.");
        await Assert.That(msg).Contains("Unauthenticated GitHub API calls hit a low rate limit.");
    }

    [Test]
    public async Task OmitsExtraWhenNull()
    {
        var msg = TokenSetupAdvice.MissingTokenMessage(
            "Polar",
            "PolarToken",
            "SponsorCheck:PolarToken",
            onBuildServer: false,
            extra: null);
        await Assert.That(msg).EndsWith("env var.");
    }

    [Test]
    public async Task DoesNotMentionConventionalCiNames()
    {
        // GITHUB_TOKEN / POLAR_API_KEY env vars do NOT auto-flow into the MSBuild properties the
        // bundler reads (case-insensitive but underscore-sensitive). Make sure the message doesn't
        // misleadingly recommend them.
        var github = TokenSetupAdvice.MissingTokenMessage(
            "GitHub Sponsors", "GitHubToken", "SponsorCheck:GitHubToken", onBuildServer: true);
        await Assert.That(github).DoesNotContain("GITHUB_TOKEN");

        var polar = TokenSetupAdvice.MissingTokenMessage(
            "Polar", "PolarToken", "SponsorCheck:PolarToken", onBuildServer: true);
        await Assert.That(polar).DoesNotContain("POLAR_API_KEY");
    }
}
