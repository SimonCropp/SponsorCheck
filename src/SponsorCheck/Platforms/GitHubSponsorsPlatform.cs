public sealed class GitHubSponsorsPlatform : ISponsorshipPlatform
{
    const string endpoint = "https://api.github.com/graphql";

    const string query = """
                         query($login: String!, $cursor: String) {
                           user(login: $login) {
                             sponsors(first: 100, after: $cursor) {
                               pageInfo { hasNextPage endCursor }
                               nodes { __typename ... on User { login } ... on Organization { login } }
                             }
                           }
                           organization(login: $login) {
                             sponsors(first: 100, after: $cursor) {
                               pageInfo { hasNextPage endCursor }
                               nodes { __typename ... on User { login } ... on Organization { login } }
                             }
                           }
                         }
                         """;

    readonly HttpClient client;

    public GitHubSponsorsPlatform() : this(HttpClientFactory.Get())
    {
    }

    public GitHubSponsorsPlatform(HttpClient client) => this.client = client;

    public string Id => "GitHubSponsors";

    public async Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        Cancel cancel)
    {
        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? userCursor = null;
        string? orgCursor = null;
        var userDone = false;
        var orgDone = false;
        var resolved = false;

        while (!(userDone && orgDone))
        {
            var json = await Post(ownerAccount, userDone ? null : userCursor, orgDone ? null : orgCursor, token, cancel).ConfigureAwait(false);
            var page = ParseResponse(json);
            if (page.UserExists || page.OrgExists)
            {
                resolved = true;
            }

            foreach (var login in page.UserLogins)
            {
                logins.Add(login);
            }

            foreach (var login in page.OrgLogins)
            {
                logins.Add(login);
            }

            if (!page.UserHasNextPage)
            {
                userDone = true;
            }
            else
            {
                userCursor = page.UserEndCursor;
            }

            if (!page.OrgHasNextPage)
            {
                orgDone = true;
            }
            else
            {
                orgCursor = page.OrgEndCursor;
            }

            if (!page.UserExists)
            {
                userDone = true;
            }

            if (!page.OrgExists)
            {
                orgDone = true;
            }
        }

        if (!resolved)
        {
            throw new MaintenanceFeeException(
                $"GitHub Sponsors: '{ownerAccount}' is neither a user nor an organization (or your token lacks visibility).");
        }

        log.LogMessage(MessageImportance.Normal, $"GitHub Sponsors: fetched {logins.Count} sponsors of '{ownerAccount}'.");
        return [.. logins];
    }

    async Task<string> Post(string login, string? userCursor, string? orgCursor, string? token, Cancel cancel)
    {
        var variables = new Dictionary<string, object?>
        {
            ["login"] = login,
            ["cursor"] = userCursor ?? orgCursor
        };
        var payload = JsonSerializer.Serialize(
            new
            {
                query = query,
                variables
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaintenanceFeeException($"GitHub GraphQL HTTP {(int) response.StatusCode}: {body}");
        }

        return body;
    }

    public readonly record struct PageResult(
        bool UserExists,
        bool OrgExists,
        IReadOnlyList<string> UserLogins,
        IReadOnlyList<string> OrgLogins,
        bool UserHasNextPage,
        bool OrgHasNextPage,
        string? UserEndCursor,
        string? OrgEndCursor);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            var fatal = new List<JsonElement>();
            foreach (var error in errors.EnumerateArray())
            {
                if (IsExpectedNotFound(error))
                {
                    continue;
                }

                fatal.Add(error);
            }

            if (fatal.Count > 0)
            {
                throw new MaintenanceFeeException($"GitHub GraphQL errors: [{string.Join(",", fatal)}]");
            }
        }

        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            throw new MaintenanceFeeException("GitHub GraphQL: missing 'data' in response.");
        }

        var (userExists, userLogins, userNext, userCursor) = ParseConnection(data, "user");
        var (orgExists, orgLogins, orgNext, orgCursor) = ParseConnection(data, "organization");
        return new(userExists, orgExists, userLogins, orgLogins, userNext, orgNext, userCursor, orgCursor);
    }

    static bool IsExpectedNotFound(JsonElement error)
    {
        if (!error.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "NOT_FOUND", StringComparison.Ordinal))
        {
            return false;
        }

        if (!error.TryGetProperty("path", out var path) ||
            path.ValueKind != JsonValueKind.Array ||
            path.GetArrayLength() == 0)
        {
            return false;
        }

        var first = path[0];
        if (first.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var name = first.GetString();
        return name is "user" or "organization";
    }

    static (bool exists, IReadOnlyList<string> logins, bool hasNext, string? cursor) ParseConnection(JsonElement data, string key)
    {
        if (!data.TryGetProperty(key, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return (false, [], false, null);
        }

        var logins = new List<string>();
        var hasNext = false;
        string? cursor = null;
        if (node.TryGetProperty("sponsors", out var sponsors))
        {
            if (sponsors.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in nodes.EnumerateArray())
                {
                    if (entry.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String)
                    {
                        var value = login.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            logins.Add(value!);
                        }
                    }
                }
            }

            if (sponsors.TryGetProperty("pageInfo", out var pageInfo))
            {
                if (pageInfo.TryGetProperty("hasNextPage", out var next) && next.ValueKind == JsonValueKind.True)
                {
                    hasNext = true;
                }

                if (pageInfo.TryGetProperty("endCursor", out var cur) && cur.ValueKind == JsonValueKind.String)
                {
                    cursor = cur.GetString();
                }
            }
        }

        return (true, logins, hasNext, cursor);
    }
}
