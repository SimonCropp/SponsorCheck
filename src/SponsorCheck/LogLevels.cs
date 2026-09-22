// How the consumer asked for SponsorCheck's non-error diagnostics to be logged. SponsorCheckMessageLevel
// covers the message-severity codes (SC017, SC029–SC031, SC059, and any code a publisher downgraded to a
// message); SponsorCheckWarningLevel covers the warnings (SC005/SC006/SC023, and any code a publisher
// downgraded to a warning). They are global properties rather than per-package metadata, so one setting
// in Directory.Build.props, a project, or the environment covers every SponsorCheck package in the build.
//
// Errors are never remapped: they are the enforcement, and a log level is not a way round it. Lowering a
// warning grants nothing new either, since NoWarn could already silence one.
public sealed record LogLevels(LogLevel? Message, LogLevel? Warning)
{
    public static LogLevels Default { get; } = new(null, null);

    // An empty value keeps the default for that kind; anything else has to name a level. The vocabulary is
    // closed on purpose. Every SponsorCheck version a build references reads the same two properties, so a
    // level added in a later release would fail the build of any consumer that also references a package
    // bundling an earlier one.
    public static bool TryParse(string message, string warning, out LogLevels levels, out List<(string Property, string Value)> invalid)
    {
        invalid = [];
        if (!TryParseLevel(message, out var messageLevel))
        {
            invalid.Add(("SponsorCheckMessageLevel", message.Trim()));
        }

        if (!TryParseLevel(warning, out var warningLevel))
        {
            invalid.Add(("SponsorCheckWarningLevel", warning.Trim()));
        }

        if (invalid.Count > 0)
        {
            levels = Default;
            return false;
        }

        levels = new(messageLevel, warningLevel);
        return true;
    }

    public static bool TryParseLevel(string raw, out LogLevel? level)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "":
                level = null;
                return true;
            case "warning":
                level = LogLevel.Warning;
                return true;
            case "high":
                level = LogLevel.High;
                return true;
            case "normal":
                level = LogLevel.Normal;
                return true;
            case "low":
                level = LogLevel.Low;
                return true;
            default:
                level = null;
                return false;
        }
    }

    // The severity and importance a rendered diagnostic is logged at. Only a message's importance means
    // anything; for an error or a warning it is returned for completeness and ignored. A kind the consumer
    // left unset keeps its severity, and a message then takes the build-server default.
    public (Severity Severity, MessageImportance Importance) Apply(Severity severity, bool onBuildServer)
    {
        var level = severity switch
        {
            Severity.Message => Message,
            Severity.Warning => Warning,
            _ => null
        };

        return level switch
        {
            null => (severity, SponsorCheckLog.DefaultImportance(onBuildServer)),
            LogLevel.Warning => (Severity.Warning, MessageImportance.High),
            LogLevel.High => (Severity.Message, MessageImportance.High),
            LogLevel.Normal => (Severity.Message, MessageImportance.Normal),
            _ => (Severity.Message, MessageImportance.Low)
        };
    }
}
