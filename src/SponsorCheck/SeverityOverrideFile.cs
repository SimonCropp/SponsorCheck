public static class SeverityOverrideFile
{
    static readonly HashSet<string> Overrideable = new(StringComparer.Ordinal)
    {
        "SC001", "SC003", "SC004", "SC005"
    };

    public static IReadOnlyList<string> OverrideableCodes =>
        Overrideable.OrderBy(_ => _, StringComparer.Ordinal).ToList();

    // Bundler-side: parse author-supplied metadata (e.g. "SC001=warning;SC003=error"). On failure
    // returns an empty dict and sets `error` to a human-readable message naming the bad token.
    public static IReadOnlyDictionary<string, Severity> ParseAuthorInput(string raw, out string? error)
    {
        error = null;
        var result = new Dictionary<string, Severity>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var token in raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || eq == trimmed.Length - 1)
            {
                error = $"SponsorCheckSeverityOverrides entry '{trimmed}' is not in 'CODE=severity' form.";
                return new Dictionary<string, Severity>(StringComparer.Ordinal);
            }

            var code = trimmed.Substring(0, eq).Trim().ToUpperInvariant();
            var severityRaw = trimmed.Substring(eq + 1).Trim();

            if (!Overrideable.Contains(code))
            {
                error = $"SponsorCheckSeverityOverrides code '{code}' is not overrideable. Allowed: {string.Join(", ", OverrideableCodes)}.";
                return new Dictionary<string, Severity>(StringComparer.Ordinal);
            }

            if (!TryParseSeverity(severityRaw, out var severity))
            {
                error = $"SponsorCheckSeverityOverrides severity '{severityRaw}' for {code} is not recognized. Allowed: error, warning, message.";
                return new Dictionary<string, Severity>(StringComparer.Ordinal);
            }

            result[code] = severity;
        }

        return result;
    }

    public static void Write(string path, IReadOnlyDictionary<string, Severity> overrides)
    {
        var lines = overrides
            .Select(_ => $"{_.Key}={_.Value.ToString().ToLowerInvariant()}")
            .OrderBy(_ => _, StringComparer.Ordinal);
        File.WriteAllLines(path, lines);
    }

    // Verifier-side: read the bundled sidecar. Tolerant of a missing file (no overrides set).
    // Silently skips malformed lines — bundler-side validation is the source of truth, so a
    // malformed file here means tampering or corruption and we fall back to default severities.
    public static IReadOnlyDictionary<string, Severity> Read(string path)
    {
        var result = new Dictionary<string, Severity>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1)
            {
                continue;
            }

            var code = line.Substring(0, eq).Trim().ToUpperInvariant();
            var severityRaw = line.Substring(eq + 1).Trim();
            if (Overrideable.Contains(code) && TryParseSeverity(severityRaw, out var severity))
            {
                result[code] = severity;
            }
        }

        return result;
    }

    static bool TryParseSeverity(string raw, out Severity severity)
    {
        switch (raw.ToLowerInvariant())
        {
            case "error":
                severity = Severity.Error;
                return true;
            case "warning":
                severity = Severity.Warning;
                return true;
            case "message":
                severity = Severity.Message;
                return true;
            default:
                severity = default;
                return false;
        }
    }
}
