namespace EnforceOssSponsorship.Tasks;

using System.Security.Cryptography;

public static class SponsorHasher
{
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
        var sb = new StringBuilder(digest.Length * 2);
        foreach (var b in digest)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}
