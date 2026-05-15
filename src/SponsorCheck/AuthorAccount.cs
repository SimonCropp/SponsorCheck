// Pairs each enabled platform's identifier and account (from AuthorAccountsPath) with both the
// public sponsor URL and the consumer-side metadata name. The verifier uses the URL in the
// "Sponsor at" block and the metadata name when rendering ready-to-paste XML examples.
public sealed record AuthorAccount(string PlatformId, string Account, string SponsorUrl, string MetadataName);
