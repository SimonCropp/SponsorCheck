// What one platform fetch produced, in the form it travels between projects. FetchSponsorListTask
// returns one per platform as an item, MSBuild serves those same items to every project in the
// build that asked for that fetch, and BundleSponsorListTask reads them back.
//
// Credentials is what lets a project decide whether the result is its own. The fetch runs in a
// build of SponsorCheck's targets file rather than in the project, so it resolves the tokens from
// what it can see there: the environment, the build's global properties and user-secrets. A token
// a project sets in its own file is not among those. So the result names the credentials it was
// fetched with, as a fingerprint, and a project whose own candidates give a different one ignores
// the result and fetches for itself, exactly as it did before there was anything to share.
//
// A failure travels the same way and for the same reason: the same account asked with the same
// credentials fails the same way, so every project reports it without asking again. That matters
// most for the failure the repeated fetch used to cause, an exhausted rate limit.
public sealed record SharedFetchResult(
    string Platform,
    string Owner,
    string Credentials,
    IReadOnlyList<string> Accounts,
    SharedFetchFailure? Failure = null)
{
    const string ownerMetadata = "Owner";
    const string credentialsMetadata = "Credentials";
    const string accountsMetadata = "Accounts";
    const string failureKindMetadata = "FailureKind";
    const string failureMessageMetadata = "FailureMessage";
    const string noCredentials = "none";
    const char accountSeparator = '\n';

    public static async Task<SharedFetchResult> Run(
        ISponsorshipPlatform platform,
        string owner,
        IReadOnlyList<TokenCandidate> tokens,
        TaskLoggingHelper log)
    {
        var credentials = Fingerprint(tokens);
        try
        {
            var accounts = await SponsorFetch.WithCandidateTokens(platform, owner, tokens, log)
                .ConfigureAwait(false);
            return new(platform.Id, owner, credentials, accounts);
        }
        // Everything, not only the platform errors: a dropped connection fails every project that
        // asks next just the same, and each of those waits out its own timeout to learn it.
        catch (Exception exception)
        {
            return new(platform.Id, owner, credentials, [], SharedFetchFailure.From(exception));
        }
    }

    // Identifies a set of candidate tokens without being one. It is compared for equality and
    // nothing else, and it ends up in a build log wherever item metadata does, so it is a
    // truncated digest: enough to tell two credentials apart, useless for recovering either.
    public static string Fingerprint(IReadOnlyList<TokenCandidate> tokens)
    {
        if (tokens.Count == 0)
        {
            return noCredentials;
        }

        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", tokens.Select(_ => _.Value))));
        return Convert.ToHexStringLower(digest, 0, 8);
    }

    public bool IsFor(string platform, string owner, string credentials) =>
        string.Equals(Platform, platform, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Owner, owner, StringComparison.Ordinal) &&
        string.Equals(Credentials, credentials, StringComparison.Ordinal);

    // Free text travels base64. Item metadata is an escaped string to MSBuild, so an account or a
    // platform's error text containing a '%' followed by two hex digits would come back as a
    // different string.
    public ITaskItem ToItem()
    {
        var item = new TaskItem(Platform);
        item.SetMetadata(ownerMetadata, Encode(Owner));
        item.SetMetadata(credentialsMetadata, Credentials);
        item.SetMetadata(accountsMetadata, Encode(string.Join(accountSeparator.ToString(), Accounts)));
        if (Failure != null)
        {
            item.SetMetadata(failureKindMetadata, Failure.Kind);
            item.SetMetadata(failureMessageMetadata, Encode(Failure.Message));
        }

        return item;
    }

    // An item that does not parse is left out, which reads as "nothing shared" and sends the
    // project to its own fetch.
    public static IReadOnlyList<SharedFetchResult> FromItems(ITaskItem[] items)
    {
        var results = new List<SharedFetchResult>(items.Length);
        foreach (var item in items)
        {
            if (TryParse(item, out var result))
            {
                results.Add(result!);
            }
        }

        return results;
    }

    static bool TryParse(ITaskItem item, out SharedFetchResult? result)
    {
        result = null;
        var platform = item.ItemSpec;
        var credentials = item.GetMetadata(credentialsMetadata);
        if (string.IsNullOrWhiteSpace(platform) ||
            string.IsNullOrWhiteSpace(credentials) ||
            !TryDecode(item.GetMetadata(ownerMetadata), out var owner) ||
            !TryDecode(item.GetMetadata(accountsMetadata), out var joinedAccounts))
        {
            return false;
        }

        SharedFetchFailure? failure = null;
        var failureKind = item.GetMetadata(failureKindMetadata);
        if (!string.IsNullOrWhiteSpace(failureKind))
        {
            if (!TryDecode(item.GetMetadata(failureMessageMetadata), out var failureMessage))
            {
                return false;
            }

            failure = new(failureKind, failureMessage);
        }

        var accounts = joinedAccounts.Split([accountSeparator], StringSplitOptions.RemoveEmptyEntries);
        result = new(platform, owner, credentials, accounts, failure);
        return true;
    }

    static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    static bool TryDecode(string? value, out string decoded)
    {
        decoded = "";
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
