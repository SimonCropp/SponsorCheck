namespace SponsorCheck.Web.Models;

/// <summary>
/// What an entered diagnostic code reveals. <see cref="Placement"/> is null when the code is
/// recognized but placement-agnostic (SC017/SC018) or author-side (SC1xx).
/// </summary>
public sealed record ScCodeClassification(
    bool Recognized,
    Placement? Placement,
    bool AuthorSide,
    string? Note)
{
    public static readonly ScCodeClassification Unrecognized = new(false, null, false, null);
}
