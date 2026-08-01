namespace SponsorCheck.Web.Models;

/// <summary>
/// Verifier severity an author can pin at pack time. <see cref="Default"/> means "leave the built-in
/// severity", so no override metadata is emitted.
/// </summary>
public enum SeverityOverride
{
    Default,
    Error,
    Warning,
    Message
}
