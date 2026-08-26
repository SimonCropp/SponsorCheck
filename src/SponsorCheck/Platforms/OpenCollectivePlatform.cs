public sealed class OpenCollectivePlatform(HttpClient? client = null) : ISponsorshipPlatform
{
    const string endpoint = "https://api.opencollective.com/graphql/v2";

    const int pageLimit = 100;

    // Open Collective's MemberRole enum has no SPONSOR value — both individual and organisation
    // contributors come back as role=BACKER, with org status distinguished by accountType in the
    // UI. Adding SPONSOR to the role filter is rejected server-side with a GRAPHQL_VALIDATION_FAILED.
    const string query = """
                         query($slug: String!, $offset: Int!) {
                           account(slug: $slug) {
                             members(role: [BACKER], limit: 100, offset: $offset) {
                               totalCount
                               nodes { account { slug } }
                             }
                           }
                         }
                         """;

    // Lazy client — see GitHubSponsorsPlatform.Client: registry construction on the verifier path
    // must not allocate an HttpClient; only a real fetch touches the network. Tests inject a stub.
    HttpClient Client => client ?? HttpClientFactory.Get();

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
            // page's `limit` rows. Using the filtered count would re-fetch overlapping ranges.
            // Terminate on a short page rather than `offset >= totalCount` because the API can
            // omit `totalCount`; ParseResponse defaults it to 0 and `offset >= 0` would exit
            // after the first non-empty page, silently dropping subsequent pages.
            offset += page.RawItemCount;
            if (page.RawItemCount < pageLimit)
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
        var payload = JsonSerializer.Serialize(
            new
            {
                query,
                variables
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("Personal-Token", token);
        }

        using var response = await Client.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        var status = (int)response.StatusCode;
        if (status == 401)
        {
            // Open Collective is the one platform where the credential is optional (it only buys
            // rate-limit headroom), so deleting the stored value is a legitimate fix here and not
            // just a way to trade one failure for another.
            throw new InvalidCredentialException(
                $"Open Collective: the configured Personal-Token was rejected (HTTP 401). Token supplied: {TokenShape.Describe(token)}. This token is optional — it only raises the rate limit on collectives with many backers — so either issue a replacement at https://opencollective.com/applications or remove the stored value entirely to fall back to anonymous access.");
        }

        if (status == 429)
        {
            // Unlike the other two platforms this one is routinely called anonymously, and the
            // anonymous ceiling is what a collective with many backers actually hits — paging
            // through the member list is several requests. So here a token is the durable fix,
            // not merely a retry.
            var fix = string.IsNullOrWhiteSpace(token)
                ? "The call was unauthenticated, and anonymous callers get a much lower ceiling than token holders — a collective with many backers pages through several requests and exhausts it. Create a Personal Token at https://opencollective.com/applications (no scopes required) and set it as the <OpenCollectiveToken> MSBuild property, or the 'OpenCollectiveToken' env var, for a durable fix."
                : "Nothing is misconfigured — re-running the build after the reset is the fix.";
            throw new RateLimitedException(
                $"Open Collective: the API rate limit is exhausted (HTTP 429). {RateLimitAdvice.ResetAdvice(response, DateTime.UtcNow)} {fix}");
        }

        throw new MaintenanceFeeException($"Open Collective GraphQL HTTP {status}: {body}");
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
                         t.ValueKind == JsonValueKind.Number
            ? t.GetInt32()
            : 0;
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
