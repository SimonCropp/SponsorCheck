public sealed class PolarPlatform(HttpClient? client = null) :
    ISponsorshipPlatform
{
    const string baseUrl = "https://api.polar.sh/v1/";

    // Lazy client — see GitHubSponsorsPlatform.Client: registry construction on the verifier path
    // must not allocate an HttpClient; only a real fetch touches the network. Tests inject a stub.
    HttpClient Client => client ?? HttpClientFactory.Get();

    public string Id => "Polar";

    public string SponsorPageUrl(string ownerAccount) =>
        $"https://polar.sh/{ownerAccount}";

    public async Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        Cancel cancel)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new MissingCredentialException(
                TokenSetupAdvice.MissingTokenMessage(
                    "Polar",
                    "PolarToken",
                    "SponsorCheck:PolarToken"));
        }

        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;
        const int limit = 100;

        while (true)
        {
            var url = $"{baseUrl}subscriptions/?organization_slug={Uri.EscapeDataString(ownerAccount)}&active=true&limit={limit}&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new("Bearer", token);
            using var response = await Client.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (status == 401)
                {
                    throw new InvalidCredentialException(
                        $"Polar: the configured token was rejected (HTTP 401). Token supplied: {TokenShape.Describe(token)}. Issue a replacement organization access token at https://polar.sh with the subscriptions:read, customers:read, and organizations:read scopes. A 401 means Polar does not recognize the credential itself, so this is not a missing-scope failure (those return 403).");
                }

                if (status == 429)
                {
                    throw new RateLimitedException(
                        $"Polar: the API rate limit is exhausted (HTTP 429). {RateLimitAdvice.ResetAdvice(response, DateTime.UtcNow)} Nothing is misconfigured — re-running the build after the reset is the fix.");
                }

                throw new MaintenanceFeeException($"Polar HTTP {status}: {body}");
            }

            var pageResult = ParseResponse(body);
            foreach (var account in pageResult.SponsorAccounts)
            {
                accounts.Add(account);
            }

            // Terminate on raw page size, not extracted account count: an item without any usable
            // identifier (no github_username, no email, no user_id) gets dropped from SponsorAccounts
            // but still counts as one of the API's `limit` rows. Using filtered count here would
            // prematurely end pagination on later pages with sparse identifiers.
            if (pageResult.RawItemCount < limit)
            {
                break;
            }

            page++;
        }

        log.LogMessage(MessageImportance.Normal, $"Polar: fetched {accounts.Count} active subscribers of '{ownerAccount}'.");
        return [.. accounts];
    }

    public readonly record struct PageResult(IReadOnlyList<string> SponsorAccounts, int RawItemCount);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var accounts = new List<string>();
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return new(accounts, 0);
        }

        var rawItemCount = 0;
        foreach (var item in items.EnumerateArray())
        {
            rawItemCount++;
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

        return new(accounts, rawItemCount);
    }

    static string? TryString(JsonElement parent, params string[] path)
    {
        var current = parent;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(key, out current))
            {
                return null;
            }

            if (current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.String)
        {
            return current.GetString();
        }

        return null;
    }
}
