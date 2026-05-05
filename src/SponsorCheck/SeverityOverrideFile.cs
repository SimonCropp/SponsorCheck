public static class SeverityOverrideFile
{
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
            if (OverrideableCodes.Codes.Contains(code) && TryParseSeverity(severityRaw, out var severity))
            {
                result[code] = severity;
            }
        }

        return result;
    }

    public static bool TryParseSeverity(string raw, out Severity severity)
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
