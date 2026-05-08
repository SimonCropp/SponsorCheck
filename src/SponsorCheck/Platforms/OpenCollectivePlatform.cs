public sealed class OpenCollectivePlatform(HttpClient client) : ISponsorshipPlatform
{
    const string endpoint = "https://api.opencollective.com/graphql/v2";
    const string query = """
        query($slug: String!, $offset: Int!) {
          account(slug: $slug) {
            members(role: BACKER, limit: 100, offset: $offset) {
              totalCount
              nodes { account { slug } }
            }
          }
        }
        """;

    public OpenCollectivePlatform() : this(HttpClientFactory.Get()) { }

    public string Id => "OpenCollective";

    public string SponsorPageUrl(string ownerAccount) =>
        $"https://opencollective.com/{ownerAccount}";

    public async Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        Cancel cancel)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (true)
        {
            var json = await Post(ownerAccount, offset, token, cancel).ConfigureAwait(false);
            var page = ParseResponse(json);
            if (!page.AccountExists)
            {
                throw new MaintenanceFeeException($"Open Collective: account '{ownerAccount}' not found.");
            }

            foreach (var slug in page.MemberSlugs)
            {
                slugs.Add(slug);
            }

            // Advance by raw node count, not filtered slug count: a node whose `account.slug`
            // is missing or empty gets dropped from MemberSlugs but still consumes one of the
            // page's `limit` rows. Using the filtered count would re-fetch overlapping ranges
            // and, if a full page filtered out, could terminate before reaching totalCount.
            offset += page.RawItemCount;
            if (page.RawItemCount == 0 ||
                offset >= page.TotalCount)
            {
                break;
            }
        }

        log.LogMessage(MessageImportance.Normal, $"Open Collective: fetched {slugs.Count} backers of '{ownerAccount}'.");
        return [.. slugs];
    }

    async Task<string> Post(string slug, int offset, string? token, Cancel cancel)
    {
        var variables = new Dictionary<string, object?>
        {
            ["slug"] = slug,
            ["offset"] = offset
        };
        var payload = JsonSerializer.Serialize(new { query = query, variables });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("Personal-Token", token);
        }

        using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaintenanceFeeException($"Open Collective GraphQL HTTP {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    public readonly record struct PageResult(bool AccountExists, IReadOnlyList<string> MemberSlugs, int RawItemCount, int TotalCount);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors) &&
            errors.GetArrayLength() > 0)
        {
            throw new MaintenanceFeeException($"Open Collective GraphQL errors: {errors}");
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("account", out var account) ||
            account.ValueKind == JsonValueKind.Null)
        {
            return new(false, [], 0, 0);
        }

        if (!account.TryGetProperty("members", out var members))
        {
            return new(true, [], 0, 0);
        }

        var totalCount = members.TryGetProperty("totalCount", out var t) &&
                         t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
        var slugs = new List<string>();
        var rawItemCount = 0;
        if (members.TryGetProperty("nodes", out var nodes) &&
            nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                rawItemCount++;
                if (!node.TryGetProperty("account", out var memberAccount) ||
                    memberAccount.ValueKind != JsonValueKind.Object ||
                    !memberAccount.TryGetProperty("slug", out var slug) ||
                    slug.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = slug.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    slugs.Add(value!);
                }
            }
        }

        return new(true, slugs, rawItemCount, totalCount);
    }
}
