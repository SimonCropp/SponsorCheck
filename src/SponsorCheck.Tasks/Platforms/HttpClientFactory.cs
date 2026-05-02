namespace SponsorCheck.Tasks.Platforms;

internal static class HttpClientFactory
{
    static readonly Lazy<HttpClient> Shared = new(() =>
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "SponsorCheck/1.0");
        return client;
    });

    public static HttpClient Get() => Shared.Value;
}
