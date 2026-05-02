public static class SponsorHasher
{
    // 48-bit truncation. SponsorshipIgnored is the bypass; the hash only needs
    // to make accidental collisions vanishingly unlikely, not resist preimage attacks.
    const int hashByteLength = 6;

    public static string Hash(string platformId, string account)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            throw new ArgumentException("platformId required", nameof(platformId));
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            throw new ArgumentException("account required", nameof(account));
        }

        var input = $"{platformId}:{account.Trim().ToLowerInvariant()}";
        var bytes = Encoding.UTF8.GetBytes(input);
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hashByteLength * 2);
        for (var i = 0; i < hashByteLength; i++)
        {
            builder.Append(digest[i].ToString("x2"));
        }

        return builder.ToString();
    }
}
