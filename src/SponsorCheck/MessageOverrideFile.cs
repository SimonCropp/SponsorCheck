public static class MessageOverrideFile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Write(string path, IReadOnlyDictionary<string, string> overrides)
    {
        // Sort keys ordinal so the file is deterministic (matters for reproducible nupkgs).
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            sorted[pair.Key] = pair.Value;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(sorted, Options));
    }

    // Verifier-side: read the bundled sidecar. Tolerant of a missing file (no overrides) and of
    // entries for unknown codes (silently filtered — tampering/corruption falls back to defaults).
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
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
            var code = pair.Key.ToUpperInvariant();
            if (OverrideableCodes.Codes.Contains(code) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[code] = pair.Value;
            }
        }

        return result;
    }
}
