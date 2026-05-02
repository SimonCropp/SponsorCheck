namespace SponsorCheck.Tasks.Platforms;

using System.Net.Http.Headers;

public sealed class OpenCollectivePlatform : ISponsorshipPlatform
{
    const string Endpoint = "https://api.opencollective.com/graphql/v2";
    const string Query = """
        query($slug: String!, $offset: Int!) {
          account(slug: $slug) {
            members(role: BACKER, limit: 100, offset: $offset) {
              totalCount
              nodes { account { slug } }
            }
          }
        }
        """;

    readonly HttpClient client;

    public OpenCollectivePlatform() : this(HttpClientFactory.Get()) { }
    public OpenCollectivePlatform(HttpClient client) => this.client = client;

    public string Id => "OpenCollective";

    public async Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        CancellationToken cancellation)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        var resolved = false;
        while (true)
        {
            var json = await Post(ownerAccount, offset, token, cancellation).ConfigureAwait(false);
            var page = ParseResponse(json);
            if (!page.AccountExists)
            {
                throw new MaintenanceFeeException($"Open Collective: account '{ownerAccount}' not found.");
            }

            resolved = true;
            foreach (var slug in page.MemberSlugs)
            {
                slugs.Add(slug);
            }

            offset += page.MemberSlugs.Count;
            if (page.MemberSlugs.Count == 0 || offset >= page.TotalCount)
            {
                break;
            }
        }

        if (!resolved)
        {
            throw new MaintenanceFeeException($"Open Collective: failed to resolve '{ownerAccount}'.");
        }

        log.LogMessage(MessageImportance.Normal, $"Open Collective: fetched {slugs.Count} backers of '{ownerAccount}'.");
        return [.. slugs];
    }

    async Task<string> Post(string slug, int offset, string? token, CancellationToken cancellation)
    {
        var variables = new Dictionary<string, object?>
        {
            ["slug"] = slug,
            ["offset"] = offset
        };
        var payload = JsonSerializer.Serialize(new { query = Query, variables });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("Personal-Token", token);
        }

        using var response = await client.SendAsync(request, cancellation).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaintenanceFeeException($"Open Collective GraphQL HTTP {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    public readonly record struct PageResult(bool AccountExists, IReadOnlyList<string> MemberSlugs, int TotalCount);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            throw new MaintenanceFeeException($"Open Collective GraphQL errors: {errors}");
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("account", out var account) ||
            account.ValueKind == JsonValueKind.Null)
        {
            return new PageResult(false, [], 0);
        }

        if (!account.TryGetProperty("members", out var members))
        {
            return new PageResult(true, [], 0);
        }

        var totalCount = members.TryGetProperty("totalCount", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
        var slugs = new List<string>();
        if (members.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.TryGetProperty("account", out var memberAccount) &&
                    memberAccount.TryGetProperty("slug", out var slug) &&
                    slug.ValueKind == JsonValueKind.String)
                {
                    var value = slug.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        slugs.Add(value!);
                    }
                }
            }
        }

        return new PageResult(true, slugs, totalCount);
    }
}
