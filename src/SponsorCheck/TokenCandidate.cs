/// A resolved credential paired with a description of where it was read from. The same token name
/// reaches the bundler from an env var on CI and from user-secrets locally, so a rejection (SC107)
/// is only actionable if it names the stored value that has to be replaced.
public readonly record struct TokenCandidate(string Value, string Source);
