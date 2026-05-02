namespace SponsorCheck.Tasks.Platforms;

public static class PlatformRegistry
{
    static readonly Dictionary<string, ISponsorshipPlatform> Platforms = BuildBuiltins();

    static Dictionary<string, ISponsorshipPlatform> BuildBuiltins()
    {
        var dict = new Dictionary<string, ISponsorshipPlatform>(StringComparer.OrdinalIgnoreCase);
        Add(dict, new GitHubSponsorsPlatform());
        Add(dict, new OpenCollectivePlatform());
        Add(dict, new PolarPlatform());
        return dict;
    }

    static void Add(Dictionary<string, ISponsorshipPlatform> dict, ISponsorshipPlatform platform) =>
        dict[platform.Id] = platform;

    public static ISponsorshipPlatform Get(string id)
    {
        if (Platforms.TryGetValue(id, out var platform))
        {
            return platform;
        }

        throw new MaintenanceFeeException(
            $"Unknown sponsorship platform '{id}'. Built-in platforms: {string.Join(", ", Platforms.Keys)}.");
    }
}
