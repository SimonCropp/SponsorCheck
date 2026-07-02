public static class SponsorHasher
{
    // 48-bit truncation. SponsorshipLicenseIgnored is the bypass; the hash only needs
    // to make accidental collisions vanishingly unlikely, not resist preimage attacks.
    const int hashByteLength = 6;

    public static string Hash(string platformId, string account)
    {
        Validate(platformId, account);
        using var sha = SHA256.Create();
        return HashWith(sha, platformId, account);
    }

    // Hashes a whole entry list reusing a single SHA256 instance. The bundler hashes one entry per
    // sponsor, so this avoids a SHA256.Create()/Dispose() per entry. (The static SHA256.HashData
    // would not help on the shipping netstandard2.0/net472 assembly: there it resolves to the
    // Polyfill shim, which is itself a per-call SHA256.Create().) ComputeHash re-initializes between
    // calls, so the batch yields byte-for-byte the same hashes as calling Hash per entry.
    public static IReadOnlyList<string> HashAll(IReadOnlyList<SponsorEntry> entries)
    {
        using var sha = SHA256.Create();
        var result = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            Validate(entry.Platform, entry.Account);
            result.Add(HashWith(sha, entry.Platform, entry.Account));
        }

        return result;
    }

    static string HashWith(SHA256 sha, string platformId, string account)
    {
        var input = $"{platformId}:{account.Trim().ToLowerInvariant()}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var digest = sha.ComputeHash(bytes);
        return Convert.ToHexStringLower(digest, 0, hashByteLength);
    }

    static void Validate(string platformId, string account)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            throw new ArgumentException("platformId required", nameof(platformId));
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            throw new ArgumentException("account required", nameof(account));
        }
    }
}
