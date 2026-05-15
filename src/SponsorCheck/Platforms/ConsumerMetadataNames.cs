// Maps platform Id (e.g. "GitHubSponsors") to the consumer-side metadata attribute the
// PackageReference / PackageVersion item carries (e.g. "GitHubSponsorAccount"). Hardcoded
// because it has to match the literal attribute names baked into ConsumerVerifier.targets.
public static class ConsumerMetadataNames
{
    static readonly Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GitHubSponsors"] = "GitHubSponsorAccount",
        ["OpenCollective"] = "OpenCollectiveSponsorAccount",
        ["Polar"] = "PolarSponsorAccount"
    };

    public static string For(string platformId) =>
        map.TryGetValue(platformId, out var name) ? name : $"{platformId}SponsorAccount";

    public static IReadOnlyList<string> All { get; } = [.. map.Values];
}
