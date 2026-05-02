namespace EnforceOssSponsorship.Tasks.Platforms;

using System.Net.Http.Headers;

public sealed class PolarPlatform : ISponsorshipPlatform
{
    const string BaseUrl = "https://api.polar.sh/v1/";

    readonly HttpClient client;

    public PolarPlatform() : this(HttpClientFactory.Get()) { }
    public PolarPlatform(HttpClient client) => this.client = client;

    public string Id => "Polar";

    public async Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new MaintenanceFeeException(
                "Polar: API token required. Set <PolarToken> MSBuild property or POLAR_API_KEY env var. (EOSS103)");
        }

        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;
        const int limit = 100;
        var resolved = false;

        while (true)
        {
            var url = $"{BaseUrl}subscriptions/?organization_slug={Uri.EscapeDataString(ownerAccount)}&active=true&limit={limit}&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellation).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new MaintenanceFeeException($"Polar HTTP {(int)response.StatusCode}: {body}");
            }

            var pageResult = ParseResponse(body);
            resolved = true;
            foreach (var account in pageResult.SponsorAccounts)
            {
                accounts.Add(account);
            }

            if (pageResult.SponsorAccounts.Count < limit)
            {
                break;
            }

            page++;
        }

        if (!resolved)
        {
            throw new MaintenanceFeeException($"Polar: failed to resolve subscriptions for '{ownerAccount}'.");
        }

        log.LogMessage(MessageImportance.Normal, $"Polar: fetched {accounts.Count} active subscribers of '{ownerAccount}'.");
        return [.. accounts];
    }

    public readonly record struct PageResult(IReadOnlyList<string> SponsorAccounts);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var accounts = new List<string>();
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new PageResult(accounts);
        }

        foreach (var item in items.EnumerateArray())
        {
            // Prefer explicit GitHub username if Polar exposes it, fall back to email, then customer id.
            var account = TryString(item, "user", "github_username")
                ?? TryString(item, "customer", "github_username")
                ?? TryString(item, "user", "email")
                ?? TryString(item, "customer", "email")
                ?? TryString(item, "user_id");
            if (!string.IsNullOrWhiteSpace(account))
            {
                accounts.Add(account!);
            }
        }

        return new PageResult(accounts);
    }

    static string? TryString(JsonElement parent, params string[] path)
    {
        var current = parent;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return null;
            }

            if (current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
