public static class SponsorCheckLog
{
    const string Subcategory = "SponsorCheck";
    const string DocsBaseUrl = "https://github.com/SimonCropp/SponsorCheck/blob/main/docs/";

    public static void Error(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Error, message);

    public static void Warning(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Warning, message);

    public static void HighMessage(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Message, message);

    // Emit honoring an author-supplied override. Returns true if the build can continue
    // (severity is Warning or Message); false if it was emitted as an Error.
    public static bool Emit(TaskLoggingHelper log, string code, Severity defaultSeverity, IReadOnlyDictionary<string, Severity>? overrides, string message)
    {
        var severity = overrides != null && overrides.TryGetValue(code, out var s) ? s : defaultSeverity;
        EmitInternal(log, code, severity, message);
        return severity != Severity.Error;
    }

    static void EmitInternal(TaskLoggingHelper log, string code, Severity severity, string message)
    {
        var fullMessage = $"{NameFor(code)}. {message} See: {DocsUrl(code)}";
        switch (severity)
        {
            case Severity.Error:
                log.LogError(Subcategory, code, "", "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Warning:
                log.LogWarning(Subcategory, code, "", "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Message:
                log.LogMessage(Subcategory, code, "", "", 0, 0, 0, 0, MessageImportance.High, fullMessage);
                break;
        }
    }

    public static string DocsUrl(string code)
    {
        var doc = code.StartsWith("SC1", StringComparison.Ordinal)
            ? "BundlerDiagnosticCodes.md"
            : "VerifierDiagnosticCodes.md";
        return $"{DocsBaseUrl}{doc}#{code.ToLowerInvariant()}";
    }

    public static string NameFor(string code) => code switch
    {
        "SC001" => "No license specified",
        "SC002" => "Conflicting license modes",
        "SC003" => "License ignored",
        "SC004" => "Invalid account",
        "SC005" => "License expired",
        "SC006" => "Metadata set on both PackageReference and PackageVersion",
        "SC007" => "Invalid license date format",
        "SC008" => "Sponsorship attestation trusted",
        "SC009" => "Bundled sponsor hash file missing",
        "SC010" => "Invalid SponsorshipStart format",
        "SC011" => "SponsorshipStart in the future",
        "SC100" => "Platform fetch failed",
        "SC101" => "No platform account configured",
        "SC102" => "Missing platform credential",
        "SC103" => "User-secrets read failed",
        "SC104" => "Invalid severity override",
        _ => code
    };
}
