// Read and written with Utf8JsonWriter and JsonDocument, not JsonSerializer. The serializer works
// by reflection, which the trimmer that runs over this assembly (Squash) cannot follow, and reports
// as IL2026 at each call. The file is a flat object of strings, so nothing is lost by spelling it
// out, and what Write produces is byte for byte what the serializer did.
public static class MessageOverrideFile
{
    public static void Write(string path, IReadOnlyDictionary<string, string> overrides)
    {
        // Sort keys ordinal so the file is deterministic (matters for reproducible nupkgs).
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            sorted[pair.Key] = pair.Value;
        }

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new() {Indented = true});
        writer.WriteStartObject();
        foreach (var pair in sorted)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
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

        if (!TryParse(text, out var parsed))
        {
            return result;
        }

        foreach (var pair in parsed)
        {
            var code = pair.Key.ToUpperInvariant();
            if (OverrideableCodes.Codes.Contains(code) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[code] = pair.Value!;
            }
        }

        return result;
    }

    // All or nothing, as deserializing to a dictionary of strings was: a file that is not an
    // object, or holds a value that is neither a string nor null, yields no overrides at all,
    // not the entries that happened to be well formed.
    static bool TryParse(string text, out Dictionary<string, string?> parsed)
    {
        parsed = new(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    return false;
                }

                parsed[property.Name] = property.Value.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
