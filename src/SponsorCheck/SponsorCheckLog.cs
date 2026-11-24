public static class SponsorCheckLog
{
    const string subcategory = "SponsorCheck";
    const string docsBaseUrl = "https://github.com/SimonCropp/SponsorCheck/blob/main/docs/";

    // Marks a diagnostic that records a declaration the verifier accepted — the audit messages and
    // the ignored-license warning — rather than one it rejected. The generated targets ask a
    // project's references about coverage only to suppress a rejection, so this is what lets an
    // accepted claim skip that walk.
    //
    // It rides on the event as its help keyword because the verifier renders into
    // CapturingBuildEngine and an event is all that crosses that boundary; the keyword is the one
    // field nothing else here uses. It never reaches a build: the task replays a captured
    // diagnostic through EmitRendered, which sets none. Deciding it here, where each diagnostic's
    // default severity is declared, is what keeps it from drifting — a separate list of codes
    // would need updating with every new audit message, and forgetting would cost nothing but
    // build time, so nobody would notice.
    public const string AcceptedClaimKeyword = "SponsorCheck.AcceptedClaim";

    public static void Error(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Error, message, acceptedClaim: false);

    public static void Warning(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Warning, message, acceptedClaim: false);

    // Every message-severity diagnostic is the audit record of a claim that was accepted.
    public static void HighMessage(TaskLoggingHelper log, string code, string message) =>
        EmitInternal(log, code, Severity.Message, message, acceptedClaim: true);

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
        // Accepted means the build is let through by default, which a rejection never is. Both
        // halves matter: a publisher who downgrades the missing-config error has not made that
        // project's declaration any more present, and one who raises the ignored-license warning to
        // an error has turned an accepted claim into a rejection.
        var acceptedClaim = defaultSeverity != Severity.Error &&
                            severity != Severity.Error;
        EmitInternal(log, code, severity, message, acceptedClaim);
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

    // The verifier logs into a capture and applies the consumer's log levels to what it caught, so the
    // importance chosen here only reaches a build from the bundler, which logs no messages through it.
    static void EmitInternal(TaskLoggingHelper log, string code, Severity severity, string message, bool acceptedClaim)
    {
        var helpKeyword = acceptedClaim ? AcceptedClaimKeyword : "";
        Log(log, code, severity, DefaultImportance(BuildServerDetector.Detected), Wrap(code, severity, message), helpKeyword);
    }

    // The console logger repeats the location, code and project on every line of a multi-line
    // message, so a one-line message keeps its See link on that line. Messages are the audit trail
    // logged on passing builds, and the blank separator plus a line for the link turned each record
    // into three lines of log, the middle one empty. Errors and warnings keep the link apart from
    // their remediation block, as does a message whose body already spans lines — a publisher can
    // downgrade SC009 to a message, and its body ends in a sponsor URL of its own.
    static string Wrap(string code, Severity severity, string message)
    {
        if (severity == Severity.Message &&
            !message.Contains('\n'))
        {
            return $"{NameFor(code)}. {message} See: {DocsUrl(code)}";
        }

        return $"{NameFor(code)}. {message}\n\nSee: {DocsUrl(code)}";
    }

    // Emits a body that has already been through the wrapping above, at a severity and importance
    // already resolved against the consumer's log levels. The verifier takes this entry point for
    // both halves of the once-per-build round trip: the announce run replays what the deferred verify
    // run captured, so re-wrapping would double the name prefix and the See link. The importance only
    // applies to a message.
    public static void EmitRendered(TaskLoggingHelper log, string code, Severity severity, MessageImportance importance, string fullMessage) =>
        Log(log, code, severity, importance, fullMessage, helpKeyword: "");

    static void Log(TaskLoggingHelper log, string code, Severity severity, MessageImportance importance, string fullMessage, string helpKeyword)
    {
        switch (severity)
        {
            case Severity.Error:
                log.LogError(subcategory, code, helpKeyword, "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Warning:
                log.LogWarning(subcategory, code, helpKeyword, "", 0, 0, 0, 0, fullMessage);
                break;
            case Severity.Message:
                log.LogMessage(subcategory, code, helpKeyword, "", 0, 0, 0, 0, importance, fullMessage);
                break;
        }
    }

    // The importance of a message-severity code when the consumer set no SponsorCheckMessageLevel.
    // Those codes (SC017, SC029/SC030/SC031, SC059) are an audit trail: which carve-out was claimed,
    // or that an unverifiable attestation was trusted. The place that record is worth keeping is the
    // CI log, which is what anyone reviews after the fact — so on a build server they stay high
    // importance and show at the default minimal verbosity. On a developer machine the same line
    // repeats on every build while nothing is being kept, so it drops to low: still there under
    // `-v detailed` for anyone looking, gone from the default log.
    //
    // Takes the flag rather than reading BuildServerDetector, as with TokenSetupAdvice.MissingTokenMessage:
    // tests pin it rather than flipping real CI env vars, which the detector reads once into a static.
    public static MessageImportance DefaultImportance(bool onBuildServer)
    {
        if (onBuildServer)
        {
            return MessageImportance.High;
        }

        return MessageImportance.Low;
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
        "SC021" => "No license specified",
        "SC022" => "Conflicting license modes",
        "SC023" => "License ignored",
        "SC024" => "Invalid account",
        "SC025" => "License expired",
        "SC026" => "Invalid license date format",
        "SC027" => "Invalid SponsorshipStart format",
        "SC028" => "SponsorshipStart in the future",
        "SC029" => "Exemption claimed",
        "SC030" => "Exemption claimed",
        "SC031" => "Exemption claimed",
        "SC032" => "Unknown exemption",
        "SC033" => "Unknown exemption",
        "SC034" => "Unknown exemption",
        "SC035" => "License too far in the future",
        "SC036" => "License too far in the future",
        "SC037" => "License too far in the future",
        "SC038" => "Exemption end date required",
        "SC039" => "Exemption end date required",
        "SC040" => "Exemption end date required",
        "SC041" => "Invalid exemption end date format",
        "SC042" => "Invalid exemption end date format",
        "SC043" => "Invalid exemption end date format",
        "SC044" => "Exemption end date too far in the future",
        "SC045" => "Exemption end date too far in the future",
        "SC046" => "Exemption end date too far in the future",
        "SC047" => "Exemption expired",
        "SC048" => "Exemption expired",
        "SC049" => "Exemption expired",
        "SC050" => "Invalid private sponsorship end date format",
        "SC051" => "Invalid private sponsorship end date format",
        "SC052" => "Invalid private sponsorship end date format",
        "SC053" => "Private sponsorship end date too far in the future",
        "SC054" => "Private sponsorship end date too far in the future",
        "SC055" => "Private sponsorship end date too far in the future",
        "SC056" => "Private sponsorship declaration expired",
        "SC057" => "Private sponsorship declaration expired",
        "SC058" => "Private sponsorship declaration expired",
        "SC059" => "Private sponsorship attestation trusted",
        "SC060" => "Invalid log level",
        "SC100" => "Platform fetch failed",
        "SC101" => "No platform account configured",
        "SC102" => "Missing platform credential",
        "SC103" => "User-secrets read failed",
        "SC104" => "Invalid severity override",
        "SC105" => "Invalid SponsorOwner",
        "SC106" => "Invalid exemption definition",
        "SC107" => "Platform credential rejected",
        "SC108" => "Platform rate limit exhausted",
        "SC109" => "Invalid PrivateSponsorMaxTermMonths",
        _ => code
    };
}
