public static class SponsorCheckLog
{
    const string Subcategory = "SponsorCheck";
    const string DocsBaseUrl = "https://github.com/SimonCropp/SponsorCheck/blob/main/docs/";

    public static void Error(TaskLoggingHelper log, string code, string message) =>
        log.LogError(Subcategory, code, "", "", 0, 0, 0, 0, AppendDocsLink(code, message));

    public static void Warning(TaskLoggingHelper log, string code, string message) =>
        log.LogWarning(Subcategory, code, "", "", 0, 0, 0, 0, AppendDocsLink(code, message));

    public static void HighMessage(TaskLoggingHelper log, string code, string message) =>
        log.LogMessage(Subcategory, code, "", "", 0, 0, 0, 0, MessageImportance.High, AppendDocsLink(code, message));

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
