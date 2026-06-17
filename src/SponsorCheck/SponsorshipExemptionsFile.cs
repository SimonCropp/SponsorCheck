public static class SponsorshipExemptionsFile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Write(string path, IReadOnlyDictionary<string, string> exemptions)
    {
        // Sort keys ordinal so the file is deterministic (matters for reproducible nupkgs).
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in exemptions)
        {
            sorted[pair.Key] = pair.Value;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(sorted, Options));
    }

    // Verifier-side: read the bundled exemption sidecar. Returns an OrdinalIgnoreCase dictionary
    // so the consumer's claimed name matches the publisher's definition regardless of casing,
    // while the stored keys preserve the publisher's original casing for rendering. Tolerant of
    // a missing/empty/invalid file (treated as "no exemptions defined").
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result;
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
        }
        catch (JsonException)
        {
            return result;
        }

        if (parsed == null)
        {
            return result;
        }

        foreach (var pair in parsed)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }
}
