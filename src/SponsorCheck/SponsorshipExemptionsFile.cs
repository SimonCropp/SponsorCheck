public static class SponsorshipExemptionsFile
{
    public static void Write(string path, IReadOnlyDictionary<string, ExemptionDefinition> exemptions)
    {
        // Sort keys ordinal so the file is deterministic (matters for reproducible nupkgs).
        var sorted = new SortedDictionary<string, ExemptionDefinition>(StringComparer.Ordinal);
        foreach (var pair in exemptions)
        {
            sorted[pair.Key] = pair.Value;
        }

        // Written with Utf8JsonWriter rather than JsonSerializer so the optional maxTermMonths
        // is simply absent when unset, instead of a null the readers would have to skip.
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions {Indented = true});
        writer.WriteStartObject();
        foreach (var pair in sorted)
        {
            writer.WriteStartObject(pair.Key);
            writer.WriteString("message", pair.Value.Message);
            if (pair.Value.MaxTermMonths is { } maxTermMonths)
            {
                writer.WriteNumber("maxTermMonths", maxTermMonths);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    // Verifier-side: read the bundled exemption sidecar. Returns an OrdinalIgnoreCase dictionary
    // so the consumer's claimed name matches the publisher's definition regardless of casing,
    // while the stored keys preserve the publisher's original casing for rendering. Tolerant of
    // a missing/empty/invalid file (treated as "no exemptions defined").
    public static IReadOnlyDictionary<string, ExemptionDefinition> Read(string path)
    {
        var result = new Dictionary<string, ExemptionDefinition>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result;
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(property.Name) &&
                    TryReadDefinition(property.Value, out var definition))
                {
                    result[property.Name] = definition!;
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, ExemptionDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    // Two accepted shapes. The object form is what Write emits. The bare-string form predates
    // MaxTermMonths and is still read because nupkgs packed by an earlier SponsorCheck carry it —
    // the wizard inspects arbitrary published packages, not just ones packed by this version.
    static bool TryReadDefinition(JsonElement element, out ExemptionDefinition? definition)
    {
        definition = null;
        if (element.ValueKind == JsonValueKind.String)
        {
            var message = element.GetString();
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            definition = new(message!);
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var objectMessage = messageElement.GetString();
        if (string.IsNullOrWhiteSpace(objectMessage))
        {
            return false;
        }

        int? maxTermMonths = null;
        if (element.TryGetProperty("maxTermMonths", out var monthsElement) &&
            monthsElement.ValueKind == JsonValueKind.Number &&
            monthsElement.TryGetInt32(out var months) &&
            months > 0)
        {
            maxTermMonths = months;
        }

        definition = new(objectMessage!, maxTermMonths);
        return true;
    }
}
