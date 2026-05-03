public class LiveTokenResolverTests
{
    static IReadOnlyDictionary<string, string> Empty { get; } = new Dictionary<string, string>();

    [Test]
    public async Task EnvVarWinsOverSecret()
    {
        var secrets = new Dictionary<string, string>
        {
            ["SponsorCheck:GitHubToken"] = "from-secrets"
        };
        var token = LiveTokenResolver.Resolve("from-env", secrets, "SponsorCheck:GitHubToken");
        await Assert.That(token).IsEqualTo("from-env");
    }

    [Test]
    public async Task FallsBackToSecretWhenEnvNull()
    {
        var secrets = new Dictionary<string, string>
        {
            ["SponsorCheck:GitHubToken"] = "from-secrets"
        };
        var token = LiveTokenResolver.Resolve(null, secrets, "SponsorCheck:GitHubToken");
        await Assert.That(token).IsEqualTo("from-secrets");
    }

    [Test]
    public async Task FallsBackToSecretWhenEnvWhitespace()
    {
        var secrets = new Dictionary<string, string>
        {
            ["SponsorCheck:GitHubToken"] = "from-secrets"
        };
        var token = LiveTokenResolver.Resolve("   ", secrets, "SponsorCheck:GitHubToken");
        await Assert.That(token).IsEqualTo("from-secrets");
    }

    [Test]
    public async Task ReturnsNullWhenBothMissing()
    {
        var token = LiveTokenResolver.Resolve(null, Empty, "SponsorCheck:GitHubToken");
        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task ReturnsNullWhenSecretValueWhitespace()
    {
        var secrets = new Dictionary<string, string>
        {
            ["SponsorCheck:GitHubToken"] = "   "
        };
        var token = LiveTokenResolver.Resolve(null, secrets, "SponsorCheck:GitHubToken");
        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task SkipMessage_OnBuildServer_LeadsWithEnvVar()
    {
        var msg = LiveTokenResolver.BuildSkipMessage(
            "GitHubToken",
            "SponsorCheck:GitHubToken",
            "GitHub Sponsors",
            onBuildServer: true);
        await Assert.That(msg).Contains("GitHub Sponsors");
        await Assert.That(msg).Contains("env var 'GitHubToken'");
        // user-secrets should still be mentioned, but secondary.
        await Assert.That(msg.IndexOf("env var", StringComparison.Ordinal))
            .IsLessThan(msg.IndexOf("user-secrets", StringComparison.Ordinal));
    }

    [Test]
    public async Task SkipMessage_LocalDev_LeadsWithUserSecrets()
    {
        var msg = LiveTokenResolver.BuildSkipMessage(
            "PolarToken",
            "SponsorCheck:PolarToken",
            "Polar",
            onBuildServer: false);
        await Assert.That(msg).Contains("Polar");
        await Assert.That(msg).Contains("dotnet user-secrets set SponsorCheck:PolarToken");
        await Assert.That(msg).Contains("env var 'PolarToken'");
        await Assert.That(msg.IndexOf("user-secrets", StringComparison.Ordinal))
            .IsLessThan(msg.IndexOf("env var", StringComparison.Ordinal));
    }

    [Test]
    public async Task SkipMessage_AppendsLocalExtra()
    {
        var msg = LiveTokenResolver.BuildSkipMessage(
            "OpenCollectiveToken",
            "SponsorCheck:OpenCollectiveToken",
            "OpenCollective",
            onBuildServer: false,
            localExtra: "Anonymous calls hit rate limits.");
        await Assert.That(msg).Contains("Anonymous calls hit rate limits.");
    }
}
