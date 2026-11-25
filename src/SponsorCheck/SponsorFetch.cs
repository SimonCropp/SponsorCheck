// The platform fetch itself, for the two tasks that can run it: FetchSponsorListTask, once for a
// whole build, and BundleSponsorListTask, for a project that has no shared result it can use.
public static class SponsorFetch
{
    // Tries each candidate token in turn (explicit MSBuild property first, then user-secret) so
    // that a stale env-var-promoted property doesn't shadow a working user-secret token — the
    // common shape when the env var was set up before a scope was added and the user has since
    // refreshed only the user-secret. With zero candidates, falls through with null so the
    // platform throws the typed MissingCredentialException (SC102).
    public static async Task<IReadOnlyList<string>> WithCandidateTokens(
        ISponsorshipPlatform platform,
        string ownerAccount,
        IReadOnlyList<TokenCandidate> tokens,
        TaskLoggingHelper log)
    {
        if (tokens.Count == 0)
        {
            return await platform.FetchSponsorAccounts(ownerAccount, null, log, Cancel.None)
                .ConfigureAwait(false);
        }

        MaintenanceFeeException? lastError = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            try
            {
                return await platform.FetchSponsorAccounts(ownerAccount, tokens[i].Value, log, Cancel.None)
                    .ConfigureAwait(false);
            }
            catch (MaintenanceFeeException exception)
            {
                lastError = exception;
                if (i < tokens.Count - 1)
                {
                    log.LogMessage(
                        MessageImportance.Normal,
                        $"SponsorCheck: {platform.Id} candidate token #{i + 1} failed ({exception.Message.Split('\n')[0]}); trying next candidate.");
                }
            }
        }

        throw WithCredentialSources(lastError!, tokens);
    }

    // A platform knows the credential was rejected but not where it was stored, so the source is
    // grafted on here. Only for a rejection: for a transient HTTP failure naming the token source
    // would point the author at the one thing that isn't broken.
    static MaintenanceFeeException WithCredentialSources(
        MaintenanceFeeException error,
        IReadOnlyList<TokenCandidate> tokens)
    {
        if (error is not InvalidCredentialException)
        {
            return error;
        }

        if (tokens.Count == 1)
        {
            return new InvalidCredentialException($"{error.Message} The rejected credential was read from {tokens[0].Source}.");
        }

        var sources = string.Join(", then ", tokens.Select(_ => _.Source));
        return new InvalidCredentialException(
            $"{error.Message} SponsorCheck tried {tokens.Count} configured credentials in order and none were accepted: {sources}. The detail above is from the last attempt.");
    }
}
