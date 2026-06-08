using Microsoft.Build.Utilities;

// Shared try-each-token helper for the live platform tests. Mirrors what BundleSponsorListTask
// does in production: try each candidate token in turn, on MaintenanceFeeException fall through to
// the next, surface the last error if all fail. Stale env-var tokens (e.g. set up before a new
// required scope landed) shouldn't shadow a working user-secret token.
public static class LivePlatformFetcher
{
    public static async Task<IReadOnlyList<string>> FetchWithCandidateTokens(
        ISponsorshipPlatform platform,
        string ownerAccount,
        IReadOnlyList<string> tokens,
        TaskLoggingHelper log)
    {
        MaintenanceFeeException? lastError = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            try
            {
                return await platform.FetchSponsorAccounts(ownerAccount, tokens[i], log, Cancel.None);
            }
            catch (MaintenanceFeeException exception)
            {
                lastError = exception;
            }
        }

        throw lastError!;
    }
}
