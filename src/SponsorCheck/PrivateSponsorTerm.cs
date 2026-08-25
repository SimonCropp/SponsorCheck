// The cap on how far ahead a consumer may set SponsorshipPrivateUntil, shared by the bundler (which
// validates and bakes the author's value into the generated verifier targets) and the verifier
// (which falls back to the default when the targets file predates the setting).
//
// A private sponsorship can't be proven against the bundled hashes, so the claim is an honour-system
// attestation. Capping it is what turns "I sponsor privately" from a permanent opt-out into a
// decision a person has to make again every few months — the same forcing function
// SponsorshipLicensedUntil applies to a private license, and MaxTermMonths applies to an exemption.
public static class PrivateSponsorTerm
{
    public const int DefaultMaxTermMonths = 12;

    public const string AuthorMetadataName = "PrivateSponsorMaxTermMonths";
}
