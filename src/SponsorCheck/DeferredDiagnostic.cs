// A rendered consumer-side diagnostic, carried from the per-project verify run to the
// once-per-build announce run that actually logs it. See the announce round trip in
// EmbeddedTemplates/ConsumerVerifier.targets for why the two are separate runs.
public sealed record DeferredDiagnostic(string Code, Severity Severity, string Message)
{
    // The message travels as base64 because it lands in the <MSBuild> task's Properties list, and
    // that list is both split on ';' and property-expanded on the way through: a ';' would truncate
    // it into a bogus second property, and a literal $(...), @(...) or %(...) anywhere in the body
    // — an author's SponsorMessageOverride is free text, so one can be — would be substituted away
    // en route. Base64 is inert to both. It also doubles as the dedupe key for the announce build,
    // so it has to survive byte for byte or the same diagnostic would announce twice.
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Message));

    public static string Decode(string payload) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(payload.Trim()));
}
