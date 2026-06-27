public static class SponsorHasher
{
    // 48-bit truncation. SponsorshipLicenseIgnored is the bypass; the hash only needs
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
        return Convert.ToHexStringLower(digest, 0, hashByteLength);
    }
}
