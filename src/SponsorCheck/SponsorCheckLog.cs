public static class SponsorCheckLog
{
    const string subcategory = "SponsorCheck";
    const string docsBaseUrl = "https://github.com/SimonCropp/SponsorCheck/blob/main/docs/";

    public static void Error(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Error, message);

    public static void Warning(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Warning, message);

    public static void HighMessage(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Message, message);

    // Emit honoring author-supplied severity and message overrides. Returns true if the build
    // can continue (severity is Warning or Message); false if it was emitted as an Error.
    public static bool Emit(
        TaskLoggingHelper log,
        string code,
        Severity defaultSeverity,
        IReadOnlyDictionary<string, Severity>? severityOverrides,
        IReadOnlyDictionary<string, string>? messageOverrides,
        string defaultMessage)
    {
        var severity = GetSeverity(code, defaultSeverity, severityOverrides);
        var message = GetMessage(code, messageOverrides, defaultMessage);
        EmitInternal(log, code, severity, message);
        return severity != Severity.Error;
    }

    static Severity GetSeverity(string code, Severity defaultSeverity, IReadOnlyDictionary<string, Severity>? severityOverrides)
    {
        if (severityOverrides != null &&
            severityOverrides.TryGetValue(code, out var severity))
        {
            return severity;
        }

        return defaultSeverity;
    }

    static string GetMessage(string code, IReadOnlyDictionary<string, string>? messageOverrides, string defaultMessage)
    {
        if (messageOverrides != null &&
            messageOverrides.TryGetValue(code, out var message))
        {
            return message;
        }

        return defaultMessage;
    }

    static void EmitInternal(TaskLoggingHelper log, string code, Severity severity, string message)
    {
        var fullMessage = $"{NameFor(code)}. {message}\n\nSee: {DocsUrl(code)}";
        switch (severity)
        {
            case Severity.Error:
                log.LogError(subcategory, code, "", "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Warning:
                log.LogWarning(subcategory, code, "", "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Message:
                log.LogMessage(subcategory, code, "", "", 0, 0, 0, 0, MessageImportance.High, fullMessage);
                break;
        }
    }

    public static string DocsUrl(string code)
    {
        // SC0xx are consumer-side (verifier). SC1xx are author-side (bundler).
        var doc = code.StartsWith("SC1", StringComparison.Ordinal)
            ? "BundlerDiagnosticCodes.md"
            : "VerifierDiagnosticCodes.md";
        return $"{docsBaseUrl}{doc}#{code.ToLowerInvariant()}";
    }

    public static string NameFor(string code) => code switch
    {
        "SC001" => "No license specified",
        "SC003" => "Conflicting license modes",
        "SC005" => "License ignored",
        "SC007" => "Invalid account",
        "SC009" => "License expired",
        "SC019" => "Metadata set on both PackageReference and PackageVersion",
        "SC011" => "Invalid license date format",
        "SC017" => "Sponsorship attestation trusted",
        "SC018" => "Bundled sponsor hash file missing",
        "SC013" => "Invalid SponsorshipStart format",
        "SC015" => "SponsorshipStart in the future",
        "SC020" => "Sponsor metadata in the wrong location",
        "SC002" => "No license specified",
        "SC004" => "Conflicting license modes",
        "SC006" => "License ignored",
        "SC008" => "Invalid account",
        "SC010" => "License expired",
        "SC012" => "Invalid license date format",
        "SC014" => "Invalid SponsorshipStart format",
        "SC016" => "SponsorshipStart in the future",
        "SC100" => "Platform fetch failed",
        "SC101" => "No platform account configured",
        "SC102" => "Missing platform credential",
        "SC103" => "User-secrets read failed",
        "SC104" => "Invalid severity override",
        _ => code
    };
}
