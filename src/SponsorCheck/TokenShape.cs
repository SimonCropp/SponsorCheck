// Describes a credential for a diagnostic message without leaking it. Only the vendor prefix (a
// fixed published marker, not secret material), the character count, and whether the stored value
// carries surrounding whitespace are emitted — never the random body. That keeps SC107 safe to
// print in a public CI log while still distinguishing the three failures that actually happen:
// the right kind of token but a dead one, the wrong kind of token entirely, and a truncated or
// whitespace-padded paste.
public static class TokenShape
{
    public static string Describe(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "empty";
        }

        var trimmed = token!.Trim();
        var prefix = Prefix(trimmed);
        var described = prefix == null
            ? $"no recognized prefix, {trimmed.Length} chars"
            : $"{prefix}…, {trimmed.Length} chars";
        if (trimmed.Length != token.Length)
        {
            // A pasted-with-newline secret is a routine CI misconfiguration and is invisible in
            // every UI that stores it, so it is worth naming explicitly rather than leaving the
            // author to compare character counts.
            return $"{described}, stored with surrounding whitespace";
        }

        return described;
    }

    /// The `xxx_` vendor marker at the start of a token, or null when there is no such marker.
    public static string? Prefix(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var trimmed = token!.Trim();
        var underscore = trimmed.IndexOf('_');
        if (underscore <= 0)
        {
            return null;
        }

        var candidate = trimmed.Substring(0, underscore + 1);
        // Fine-grained PATs are `github_pat_<22 chars>_<59 chars>` — the marker is two segments, and
        // the body carries an underscore of its own, so neither first- nor last-underscore alone
        // isolates it.
        if (candidate == "github_" &&
            trimmed.StartsWith("github_pat_", StringComparison.Ordinal))
        {
            candidate = "github_pat_";
        }

        if (candidate.Length > 12)
        {
            return null;
        }

        foreach (var character in candidate)
        {
            if (character != '_' &&
                !char.IsLetterOrDigit(character))
            {
                return null;
            }
        }

        return candidate;
    }
}
