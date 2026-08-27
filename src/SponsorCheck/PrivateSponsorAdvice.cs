// Pack-time notice for sponsors a platform's privacy feature keeps out of the bundled list.
//
// Only the count is reported, never the accounts: the entire point of excluding them is that those
// identities don't leave the author's machine. The count still matters, because it tells the author
// how many of their sponsors will hit SC007 on the next release unless they declare
// SponsorshipPrivateUntil — without it the exclusion would be silent and look like a token or
// scope problem.
public static class PrivateSponsorAdvice
{
    public static string ExcludedMessage(string platformLabel, int count, string privacyTerm)
    {
        var subject = count == 1
            ? $"1 {privacyTerm} sponsor is"
            : $"{count} {privacyTerm} sponsors are";
        return $"{platformLabel}: {subject} excluded from the bundled list. They cannot match a bundled hash, so they need SponsorshipPrivateUntil=\"yyyy-MM\" alongside their sponsor account. See {SponsorCheckLog.DocsUrl("SC059")}.";
    }
}
