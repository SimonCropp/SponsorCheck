public sealed class GitHubSponsorsPlatform(HttpClient? client = null) :
    ISponsorshipPlatform
{
    const string endpoint = "https://api.github.com/graphql";

    // sponsorshipsAsMaintainer (not the simpler `sponsors` connection) so each sponsorship exposes
    // isOneTimePayment / isActive / createdAt. activeOnly:false includes lapsed recurring sponsors
    // and one-time payments past their window; IsValidAt filters them explicitly so the one-time
    // inclusion window is driven by SponsorCheck (one month from createdAt) rather than GitHub's
    // own "active" definition for one-time payments.
    const string query =
        """
        query($login: String!, $cursor: String) {
          user(login: $login) {
            sponsorshipsAsMaintainer(first: 100, after: $cursor, activeOnly: false, includePrivate: true) {
              pageInfo { hasNextPage endCursor }
              nodes {
                isActive
                isOneTimePayment
                createdAt
                sponsorEntity {
                  __typename
                  ... on User { login }
                  ... on Organization { login }
                }
              }
            }
          }
          organization(login: $login) {
            sponsorshipsAsMaintainer(first: 100, after: $cursor, activeOnly: false, includePrivate: true) {
              pageInfo { hasNextPage endCursor }
              nodes {
                isActive
                isOneTimePayment
                createdAt
                sponsorEntity {
                  __typename
                  ... on User { login }
                  ... on Organization { login }
                }
              }
            }
          }
        }
        """;

    public static readonly TimeSpan OneTimeWindow = TimeSpan.FromDays(30);

    // Resolve the shared HttpClient lazily. PlatformRegistry constructs all three platforms just for
    // id→URL/name mapping on the verifier's hot path; building them must not allocate an HttpClient.
    // Only an actual fetch (bundler pack time) touches the network. Tests inject a stub client.
    HttpClient Client => client ?? HttpClientFactory.Get();

    public string Id => "GitHubSponsors";

    public string SponsorPageUrl(string ownerAccount) =>
        $"https://github.com/sponsors/{ownerAccount}";

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
                    "GitHub Sponsors",
                    "GitHubToken",
                    "SponsorCheck:GitHubToken",
                    "Unauthenticated GitHub API calls hit a low rate limit and cause SC100 failures on shared CI IPs."));
        }

        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? userCursor = null;
        string? orgCursor = null;
        var userDone = false;
        var orgDone = false;
        var resolved = false;
        var now = DateTime.UtcNow;

        while (!(userDone && orgDone))
        {
            var json = await Post(
                ownerAccount,
                userDone ? null : userCursor,
                orgDone ? null : orgCursor,
                token,
                cancel).ConfigureAwait(false);
            var page = ParseResponse(json);
            if (page.UserExists || page.OrgExists)
            {
                resolved = true;
            }

            foreach (var entry in page.UserSponsorships)
            {
                if (IsValidAt(entry, now))
                {
                    logins.Add(entry.Login);
                }
            }

            foreach (var entry in page.OrgSponsorships)
            {
                if (IsValidAt(entry, now))
                {
                    logins.Add(entry.Login);
                }
            }

            if (page.UserHasNextPage)
            {
                userCursor = page.UserEndCursor;
            }
            else
            {
                userDone = true;
            }

            if (page.OrgHasNextPage)
            {
                orgCursor = page.OrgEndCursor;
            }
            else
            {
                orgDone = true;
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

    // Recurring sponsors count while their sponsorship is active. One-time sponsors count for one
    // month from the payment date — pairs with the OSS author setting GitHub's "Set minimum amount"
    // to match their min monthly tier, so a single one-time payment of at least the tier value
    // effectively earns one month of sponsor status.
    public static bool IsValidAt(SponsorshipEntry entry, DateTime now)
    {
        if (entry.IsOneTimePayment)
        {
            return entry.CreatedAt + OneTimeWindow >= now;
        }

        return entry.IsActive;
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
                query,
                variables
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new("Bearer", token);

        using var response = await Client.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new MaintenanceFeeException($"GitHub GraphQL HTTP {(int) response.StatusCode}: {body}");
    }

    public readonly record struct SponsorshipEntry(
        string Login,
        bool IsOneTimePayment,
        bool IsActive,
        DateTime CreatedAt);

    public readonly record struct PageResult(
        bool UserExists,
        bool OrgExists,
        IReadOnlyList<SponsorshipEntry> UserSponsorships,
        IReadOnlyList<SponsorshipEntry> OrgSponsorships,
        bool UserHasNextPage,
        bool OrgHasNextPage,
        string? UserEndCursor,
        string? OrgEndCursor);

    public static PageResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors) &&
            errors.GetArrayLength() > 0)
        {
            var fatal = new List<JsonElement>();
            foreach (var error in errors.EnumerateArray())
            {
                if (IsExpectedNotFound(error))
                {
                    continue;
                }

                if (IsClassicPatForbidden(error, out var orgName))
                {
                    throw new MaintenanceFeeException(
                        $"GitHub Sponsors: organization '{orgName}' has disabled access via classic personal access tokens. " +
                        "Fine-grained PATs don't expose a Sponsorships permission, so a classic PAT is the only token type " +
                        $"that can read private sponsors. Ask an admin of '{orgName}' to re-enable classic-PAT access in the " +
                        "org's personal-access-token settings, then refresh SponsorCheck:GitHubToken " +
                        "(or the GitHubToken MSBuild property / env var).");
                }

                if (IsInsufficientScopes(error))
                {
                    throw new MaintenanceFeeException(
                        "GitHub Sponsors: the configured token is missing the 'read:user' scope. " +
                        "SponsorCheck reads per-sponsorship metadata (isOneTimePayment, createdAt, isActive) " +
                        "from sponsorshipsAsMaintainer, which GitHub gates on 'read:user' even for organization " +
                        "maintainers. Edit the classic PAT at https://github.com/settings/tokens to add 'read:user' " +
                        "(keep 'read:org' alongside it if sponsored as an organization), then refresh " +
                        "SponsorCheck:GitHubToken (or the GitHubToken MSBuild property / env var).");
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

        var (userExists, userEntries, userNext, userCursor) = ParseConnection(data, "user");
        var (orgExists, orgEntries, orgNext, orgCursor) = ParseConnection(data, "organization");
        return new(userExists, orgExists, userEntries, orgEntries, userNext, orgNext, userCursor, orgCursor);
    }

    static bool IsClassicPatForbidden(JsonElement error, out string? orgName)
    {
        orgName = null;
        if (!error.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "FORBIDDEN", StringComparison.Ordinal))
        {
            return false;
        }

        if (!error.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = message.GetString();
        if (text == null ||
            text.IndexOf("personal access token (classic)", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        // Message format: "`<orgName>` forbids access via a personal access token (classic)..."
        var first = text.IndexOf('`');
        if (first >= 0)
        {
            var second = text.IndexOf('`', first + 1);
            if (second > first)
            {
                orgName = text.Substring(first + 1, second - first - 1);
            }
        }

        return true;
    }

    static bool IsInsufficientScopes(JsonElement error)
    {
        if (!error.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(type.GetString(), "INSUFFICIENT_SCOPES", StringComparison.Ordinal);
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

    static (bool exists, IReadOnlyList<SponsorshipEntry> entries, bool hasNext, string? cursor) ParseConnection(JsonElement data, string key)
    {
        if (!data.TryGetProperty(key, out var node) ||
            node.ValueKind == JsonValueKind.Null)
        {
            return (false, [], false, null);
        }

        var entries = new List<SponsorshipEntry>();
        var hasNext = false;
        string? cursor = null;
        if (node.TryGetProperty("sponsorshipsAsMaintainer", out var sponsorships))
        {
            if (sponsorships.TryGetProperty("nodes", out var nodes) &&
                nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in nodes.EnumerateArray())
                {
                    if (TryParseEntry(entry, out var parsed))
                    {
                        entries.Add(parsed);
                    }
                }
            }

            if (sponsorships.TryGetProperty("pageInfo", out var pageInfo))
            {
                if (pageInfo.TryGetProperty("hasNextPage", out var next) &&
                    next.ValueKind == JsonValueKind.True)
                {
                    hasNext = true;
                }

                if (pageInfo.TryGetProperty("endCursor", out var endCursor) &&
                    endCursor.ValueKind == JsonValueKind.String)
                {
                    cursor = endCursor.GetString();
                }
            }
        }

        return (true, entries, hasNext, cursor);
    }

    static bool TryParseEntry(JsonElement node, out SponsorshipEntry entry)
    {
        entry = default;
        if (!node.TryGetProperty("sponsorEntity", out var sponsorEntity) ||
            sponsorEntity.ValueKind != JsonValueKind.Object ||
            !sponsorEntity.TryGetProperty("login", out var login) ||
            login.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var loginValue = login.GetString();
        if (string.IsNullOrWhiteSpace(loginValue))
        {
            return false;
        }

        if (!node.TryGetProperty("createdAt", out var createdAt) ||
            createdAt.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(
                createdAt.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var when))
        {
            return false;
        }

        var isActive = node.TryGetProperty("isActive", out var active) &&
                       active.ValueKind == JsonValueKind.True;
        var isOneTime = node.TryGetProperty("isOneTimePayment", out var oneTime) &&
                        oneTime.ValueKind == JsonValueKind.True;

        entry = new(loginValue!, isOneTime, isActive, when);
        return true;
    }
}
