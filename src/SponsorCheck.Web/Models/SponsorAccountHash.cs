namespace SponsorCheck.Web.Models;

/// <summary>
/// The bundler's sponsor-account hash, restated for the wizard. Mirrors SponsorHasher in the task
/// assembly, which the wizard cannot reference: SHA256 over <c>"{platformId}:{account}"</c> with the
/// account trimmed and lowercased, truncated to 48 bits and rendered lowercase hex. Every byte of
/// that has to match or a lookup would report a sponsor missing from their own package's list —
/// <c>RepoContractTests</c> pins it against the real hasher's source, and
/// <c>SponsorAccountHashTests</c> against a value the real hasher produced.
///
/// The truncation is the bundler's, not a shortcut taken here: 48 bits makes accidental collisions
/// vanishingly unlikely, which is all the list needs, since SponsorshipLicenseIgnored is the bypass.
/// The wizard only ever hashes an account the consumer typed about themselves and compares it in the
/// browser, so nothing about the account leaves the page.
/// </summary>
public static class SponsorAccountHash
{
    const int hashByteLength = 6;

    /// <param name="platformId">The platform's wire id — <see cref="Platform.WireId"/>, which is what
    /// the bundler hashes and what SponsorCheck.AuthorAccounts.txt keys on.</param>
    /// <param name="account">The consumer's account on that platform, as typed.</param>
    public static string For(string platformId, string account)
    {
        var input = $"{platformId}:{account.Trim().ToLowerInvariant()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(digest, 0, hashByteLength);
    }
}
