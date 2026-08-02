namespace SponsorCheck.Web.Models;

/// <summary>An exemption read out of a published nupkg's SponsorCheck.Exemptions.json. A non-null
/// <paramref name="MaxTermMonths"/> means the publisher time-bounds the claim: the consumer must
/// also declare SponsorshipExemptionUntil, no further out than that many months.</summary>
public sealed record PackageExemption(string Name, string Message, int? MaxTermMonths = null);
