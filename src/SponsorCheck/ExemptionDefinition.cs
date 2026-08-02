// A publisher-defined exemption, as declared by an <SponsorExemption> item at pack time and
// bundled into SponsorCheck.Exemptions.json. Message is the criteria text that becomes the body
// of the consumer-side warning (SC029/SC030/SC031) when the exemption is claimed.
//
// MaxTermMonths turns the claim into a time-bounded one: the consumer must also supply
// SponsorshipExemptionUntil, and that month may be at most MaxTermMonths past the build clock.
// Null (the default) leaves the claim open-ended — the consumer may still self-bound it, but
// nothing forces them to. The point of a bound is that a claim which stops applying eventually
// breaks the build instead of quietly riding along forever.
public sealed record ExemptionDefinition(string Message, int? MaxTermMonths = null);
