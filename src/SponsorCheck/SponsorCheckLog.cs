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
        var fullMessage = AppendDocsLink(code, message);
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

    static string AppendDocsLink(string code, string message) =>
        $"{message} See: {DocsUrl(code)}";
}
